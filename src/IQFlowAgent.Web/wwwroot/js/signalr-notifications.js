/**
 * IQFlowAgent — SignalR notification client
 * Connects to /hubs/notifications and listens for RAG job completion events.
 * Displays a dismissible toast notification when analysis is ready.
 */
(function () {
    'use strict';

    // Only run when SignalR is available (it is loaded as a NuGet static file)
    if (typeof signalR === 'undefined') {
        console.warn('[IQFlow] SignalR client library not loaded — notifications disabled.');
        return;
    }

    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/notifications')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    // ── Event handlers ────────────────────────────────────────────────────────

    connection.on('AnalysisReady', function (data) {
        showToast(
            '✅ Analysis Ready',
            `AI analysis for <strong>${data.processName}</strong> (${data.intakeId}) is complete. ` +
            `${data.filesProcessed} file(s) processed.`,
            'success',
            `/Intake/AnalysisResult/${data.intakeDbId}`
        );
    });

    connection.on('AnalysisFailed', function (data) {
        showToast(
            '❌ Analysis Failed',
            `Processing failed for <strong>${data.intakeId}</strong>: ${data.error}`,
            'error',
            null
        );
    });

    // ── Connection lifecycle ──────────────────────────────────────────────────

    async function start() {
        try {
            await connection.start();
            console.info('[IQFlow] SignalR connected.');
            await connection.invoke('JoinUserGroup');
        } catch (err) {
            console.warn('[IQFlow] SignalR connection failed:', err);
            setTimeout(start, 10000);
        }
    }

    connection.onclose(async () => {
        console.warn('[IQFlow] SignalR disconnected.');
    });

    start();

    // ── Toast notification ────────────────────────────────────────────────────

    function showToast(title, message, type, actionUrl) {
        // Create container if it doesn't exist
        let container = document.getElementById('iqflow-toast-container');
        if (!container) {
            container = document.createElement('div');
            container.id = 'iqflow-toast-container';
            container.style.cssText =
                'position:fixed;bottom:24px;right:24px;z-index:9999;display:flex;flex-direction:column;gap:12px;max-width:380px;';
            document.body.appendChild(container);
        }

        const toast = document.createElement('div');
        const bg    = type === 'success' ? '#e8f5e9' : '#fdecea';
        const border= type === 'success' ? '#43a047' : '#e53935';
        const icon  = type === 'success' ? '✅' : '❌';

        toast.style.cssText =
            `background:${bg};border-left:4px solid ${border};border-radius:8px;` +
            `padding:14px 16px;box-shadow:0 4px 16px rgba(0,0,0,0.14);` +
            `font-size:13px;line-height:1.5;animation:slideInToast 0.3s ease;`;

        const titleEl = document.createElement('div');
        titleEl.style.cssText = 'font-weight:700;font-size:14px;margin-bottom:4px;';
        titleEl.textContent = `${icon} ${title}`;

        const msgEl = document.createElement('div');
        msgEl.innerHTML = message; // sanitised server-side; contains only intake name + id

        toast.appendChild(titleEl);
        toast.appendChild(msgEl);

        if (actionUrl) {
            const link = document.createElement('a');
            link.href = actionUrl;
            link.textContent = 'View Analysis →';
            link.style.cssText = `color:${border};font-weight:600;display:inline-block;margin-top:6px;`;
            toast.appendChild(link);
        }

        // Dismiss button
        const close = document.createElement('button');
        close.textContent = '×';
        close.style.cssText =
            'position:absolute;top:8px;right:10px;background:none;border:none;' +
            'font-size:18px;cursor:pointer;color:#888;line-height:1;';
        close.addEventListener('click', () => toast.remove());
        toast.style.position = 'relative';
        toast.appendChild(close);

        container.appendChild(toast);

        // Auto-dismiss after 12 s
        setTimeout(() => { if (toast.isConnected) toast.remove(); }, 12000);
    }

    // ── CSS animation ─────────────────────────────────────────────────────────

    const style = document.createElement('style');
    style.textContent =
        '@keyframes slideInToast{from{opacity:0;transform:translateX(80px)}to{opacity:1;transform:translateX(0)}}';
    document.head.appendChild(style);
})();
