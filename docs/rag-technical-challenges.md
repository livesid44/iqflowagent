# Top 5 Technical Challenges: RAG Document Mapping in IQFlowAgent

This document identifies the five most significant technical challenges in the current Retrieval-Augmented Generation (RAG) document mapping pipeline, with direct references to the affected code in this repository. Each challenge includes a description of the problem, the specific code involved, and the risks it introduces.

---

## Challenge 1 — No True Vector-Based Retrieval: Single-Pass Context Window Concatenation

### What the code does

`RagProcessorService` collects every document belonging to an intake and appends their extracted text into a single `StringBuilder`. That combined string is then passed in one shot to Azure OpenAI:

```csharp
// RagProcessorService.cs – ProcessRagJobAsync()
var aggregatedText = new StringBuilder();
foreach (var doc in docs)
{
    var text = await ReadDocumentTextAsync(doc, blobSvc, ct);
    aggregatedText.AppendLine($"=== File: {doc.FileName} ===");
    aggregatedText.AppendLine(text);
}
var combinedText = aggregatedText.ToString();
var analysisJson = await aiService.AnalyzeIntakeAsync(intake, combinedText);
```

Text-based files are hard-capped at 8,000 characters each before concatenation:

```csharp
// RagProcessorService.cs
private const int MaxTextCharsPerFile = 8_000;

// ReadDocumentTextAsync()
return text.Length > MaxTextCharsPerFile
    ? text[..MaxTextCharsPerFile] + "...[truncated]"
    : text;
```

### Why this is a problem

This approach is **naive full-context stuffing**, not true RAG. Real RAG splits documents into smaller semantic chunks, embeds them as vectors, and retrieves only the most relevant chunks for each query. The current design has several compounding limitations:

- **Silent content loss.** Any document exceeding 8,000 characters is truncated without warning to the user. A detailed SOP process guide, for example, could be cut in half. The AI never sees the missing content and cannot flag it as absent.
- **No prioritised retrieval.** All documents are weighted equally regardless of their relevance to specific BARTOK sections being assessed. A one-line placeholder file receives the same treatment as a 200-row Excel volumetrics sheet.
- **Context window exhaustion.** GPT-4o supports a 128k-token context window but the system imposes no guard on the total combined size of all documents. An intake with 15 uploaded files could silently exceed the limit, causing the API to return an error or to silently drop messages, falling back to a mock analysis.
- **No chunking or embedding infrastructure.** There is no vector store, no embedding model call, and no similarity search. Scaling to larger document sets requires a complete architectural rework rather than incremental improvement.

---

## Challenge 2 — Opaque Document-to-Section Mapping Without Source Attribution

### What the code does

The AI system prompt instructs GPT-4o to assess all 11 BARTOK sections against the uploaded document content:

```csharp
// AzureOpenAiService.cs – AnalyzeIntakeAsync() system prompt
"For EACH section listed above, determine whether the available information is sufficient (Pass),
partial (Warning), or missing (Fail)."
```

The model returns a flat JSON structure:

```json
{
  "checkPoints": [
    { "sectionId": "3", "label": "RACI", "status": "Pass", "note": "..." }
  ],
  "actionItems": [
    { "title": "...", "bartokSection": "2. Process Overview", "requiredInfo": "..." }
  ]
}
```

The JSON is stored verbatim in `IntakeRecord.AnalysisResult`. There are no document references, page numbers, or text excerpts in the response structure.

### Why this is a problem

- **No traceability.** When a checkpoint says `"status": "Pass"` for RACI, there is no indication of which document, which paragraph, or which table cell provided that evidence. Users cannot verify the AI's claim without manually reviewing all uploaded files.
- **Compliance risk.** BARTOK S8 SOP processes are often subject to internal audit. An opaque AI verdict with no cited source passages cannot satisfy an auditor asking "how do you know the RACI matrix is complete?"
- **Section-to-document drift.** If a user uploads a revised document after the initial analysis, there is no mechanism to identify which sections were informed by the old version versus the new one.
- **Single-model single-pass assessment.** All 11 BARTOK sections are evaluated in one prompt with a maximum output of 2,000 tokens (`AzureOpenAiService.cs: DefaultMaxOutputTokens = 2000`). If the model's reasoning for complex sections (e.g., Work Instructions or Volumetrics) is compressed to fit the token budget, the resulting assessment may be superficial.

---

## Challenge 3 — Incomplete Document Format Coverage and Lossy Text Extraction

### What the code does

`DocumentTextExtractor.Extract()` handles only `.docx` and `.xlsx`. `ReadDocumentTextAsync()` handles only plain-text extensions:

```csharp
// RagProcessorService.cs
private static readonly HashSet<string> TextExtensions =
    new(StringComparer.OrdinalIgnoreCase)
    { ".txt", ".csv", ".json", ".xml", ".md", ".log" };
```

PDF is not in either list. When a document's extension does not match any of these sets, it falls through silently:

```csharp
// RagProcessorService.cs – ReadDocumentTextAsync()
var ext = Path.GetExtension(doc.FileName).ToLowerInvariant();
if (!TextExtensions.Contains(ext)) return null;  // PDF, PPT, etc. silently return null
```

For DOCX, the extractor uses `para.InnerText` for paragraphs and joins table cells with a tab:

```csharp
// DocumentTextExtractor.cs
if (element is Paragraph para)
{
    var text = para.InnerText;   // loses bold, italic, heading styles, and list markers
    if (!string.IsNullOrWhiteSpace(text))
        sb.AppendLine(text);
}
```

`DocumentIntelligenceService.cs` (Azure Document Intelligence / OCR) exists in the services folder but is **not wired into the RAG pipeline** — it is unused during `RagProcessorService` document processing.

### Why this is a problem

- **PDF is the most common SOP format.** Users submitting BARTOK S8 documentation will frequently upload PDF files. These are accepted by the intake form (no file-type restriction enforced at upload) but produce no text for the AI. The job completes silently with zero content extracted from PDF documents.
- **Formatting context is discarded.** DOCX heading styles distinguish section titles from body text — critical for identifying which part of a document covers RACI versus Work Instructions. `InnerText` strips all styling, turning "**Section 4 – SOP Steps**" into "Section 4  SOP Steps" indistinguishable from body paragraphs.
- **Embedded objects are invisible.** Flow diagrams, process maps, embedded Excel tables in Word documents, and images containing text are all silently skipped. A process diagram embedded in a Word document may be the primary description of the SOP steps, but it contributes zero text to the analysis.
- **Azure Document Intelligence is wasted.** The OCR integration is already built (`DocumentIntelligenceService.cs`), authenticated, and configured in `appsettings.json`. It is simply not called from the RAG processor, leaving the most capable parsing tool unused for the files that need it most.

---

## Challenge 4 — PII Masking Corrupts Semantically Critical Metadata Before AI Analysis

### What the code does

Before the aggregated document text is sent to GPT-4o, it is passed through `EnforcePiiPolicyAsync`, which calls `PiiScanService.ScanWithSettings`. The scanner applies compiled regex patterns and replaces matches with generic placeholders:

```csharp
// PiiScanService.cs
Redact(ref redacted, findings, settings.DetectEmailAddresses, RxEmail, "Email Address");
Redact(ref redacted, findings, settings.DetectPersonNames, ...);
// Result: "john.doe@company.com" → "[EMAIL_ADDRESS]"
//         "Jane Smith"           → "[PERSON_NAME]"
```

The masked text is what the AI receives. Findings are logged to `IntakeRecord.PiiMaskingLog`, but the original values are never sent to the model:

```csharp
// AzureOpenAiService.cs
new { role = "user", content = await EnforcePiiPolicyAsync(BuildUserMessage(intake, documentText), "AnalyzeIntake") }
```

### Why this is a problem

The BARTOK Document Control section requires specific named individuals:

```
Document Control: Process name, lot/SDC, document date, process author (name + email), approver (name + email)
```

- **Masking breaks BARTOK Document Control assessment.** If the uploaded SOP document contains `"Author: Jane Smith, jane.smith@acme.com"`, both the name and email are redacted. The AI sees `"Author: [PERSON_NAME], [EMAIL_ADDRESS]"` and correctly identifies that a real person is named — but it cannot confirm whether the email domain matches the organisation or whether the approver is a different individual. This causes **false Pass verdicts** for Document Control.
- **Regex false positives mask business terms.** `RxPassport` matches `\b[A-Z]{1,2}\d{6,9}\b`, which also matches internal reference codes like `"OCC123456"`, `"SDC7890123"`, or contract identifiers. Masking these references can cause the AI to miss OCC obligations (BARTOK section 11).
- **The allowlist is fragile.** `_commonCapitalisedPhrases` in `PiiScanService.cs` attempts to prevent masking known business terms, but it is a static, hardcoded list. Organisation-specific role titles, department names, and product names not in the list will be masked, silently degrading analysis quality.
- **No round-trip de-masking for UI display.** The `PiiMaskingLog` stores what was redacted but there is no mechanism to re-hydrate the AI's response with real values for display in the UI. Recommendations mentioning `[PERSON_NAME]` appear verbatim to the user, making them confusing and unprofessional.

---

## Challenge 5 — Fragile Audio/Video Transcription Pipeline With No Content Feedback Loop

### What the code does

Audio and video files are transcribed via Azure Batch Transcription in `TranscribeDocumentAsync`. The pipeline has two sequential async steps: blob upload (if the file is on local disk) then transcription:

```csharp
// RagProcessorService.cs – TranscribeDocumentAsync()
await EnsureAudioInBlobAsync(doc, intake, db, blobSvc, ct);  // Step 1: upload to blob
var transcript = await speechSvc.TranscribeAsync(doc.FilePath, intake.TenantId, doc.FileName);  // Step 2: transcribe
doc.TranscriptText   = transcript;
doc.TranscriptStatus = "Complete";

var sopMarkdown = await aiService.GenerateSopFromTranscriptAsync(transcript, intake);
// SOP PDF stored as a new IntakeDocument (DocumentType = "SopDocument")
// but is NOT added back to aggregatedText for the main AI analysis
```

The generated SOP document is saved to the database and blob storage but the main analysis loop does not re-read it:

```csharp
// ProcessRagJobAsync() — only "IntakeDocument" type is loaded, not "SopDocument"
var docs = await db.IntakeDocuments
    .Where(d => d.IntakeRecordId == intake.Id && d.DocumentType == "IntakeDocument")
    .ToListAsync(ct);
```

Errors are caught per-document and execution continues:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Transcription failed for document {DocId} ({FileName}).", ...);
    doc.TranscriptStatus = "Error";
    await db.SaveChangesAsync(CancellationToken.None);
    // No re-throw — job continues without this document's content
}
```

### Why this is a problem

- **Generated SOP content is excluded from BARTOK analysis.** When a training video or process walkthrough recording is uploaded, the transcript is converted into a structured SOP markdown document (`GenerateSopFromTranscriptAsync`). That SOP — which may be the richest structured content available — is stored as a `SopDocument` and then **ignored by the subsequent `AnalyzeIntakeAsync` call** because the document query filters exclusively on `DocumentType == "IntakeDocument"`. The AI performs its BARTOK assessment without the benefit of the very document it helped produce.
- **Deferred blob upload is a silent failure point.** When Azure Blob Storage is not configured and files are stored locally, `EnsureAudioInBlobAsync` must upload the file to blob at processing time for Azure Batch Transcription to access it via a public HTTPS URL. If the blob upload fails (e.g., connectivity issue, storage quota), `TranscriptStatus` is set to `"Error"`, the file is skipped, and the job reports success — the user has no indication that their audio content was never analysed.
- **No transcript length guard.** `IntakeDocument.TranscriptText` stores the full Azure Speech output with no character cap. A two-hour meeting recording could produce a 50,000-word transcript. Unlike text files (capped at `MaxTextCharsPerFile = 8,000`), transcripts are appended to `aggregatedText` without truncation, creating an unpredictable total context size that can exceed the model's token limit.
- **No retry or resume for partial transcription failures.** If a batch of five audio files is submitted and the third one fails, the RAG job marks `TranscriptStatus = "Error"` on that document and continues. On re-submission the user must delete and re-upload all five files; there is no mechanism to retry only the failed documents within an existing job.

---

## Summary Table

| # | Challenge | Primary File(s) | Risk Level |
|---|-----------|----------------|------------|
| 1 | No vector retrieval — full-context stuffing with per-file truncation | `RagProcessorService.cs`, `AzureOpenAiService.cs` | High |
| 2 | No source attribution for document-to-BARTOK section mapping | `AzureOpenAiService.cs`, `RagProcessorService.cs` | High |
| 3 | PDF not parsed; lossy DOCX extraction; Azure Document Intelligence unused | `DocumentTextExtractor.cs`, `RagProcessorService.cs`, `DocumentIntelligenceService.cs` | High |
| 4 | PII masking corrupts BARTOK-critical metadata before AI analysis | `PiiScanService.cs`, `AzureOpenAiService.cs` | Medium |
| 5 | Audio SOP output excluded from analysis; no transcript size guard; no retry | `RagProcessorService.cs` | Medium |
