"use strict";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/notifications")
    .withAutomaticReconnect()
    .build();

connection.on("ReceiveNotification", function (message) {
    showToast(message);
});

function showToast(message) {
    let container = document.getElementById("toast-container");
    if (!container) {
        container = document.createElement("div");
        container.id = "toast-container";
        container.className = "toast-container position-fixed top-0 end-0 p-3";
        container.style.zIndex = "1080";
        document.body.appendChild(container);
    }

    const toastId = "toast-" + Date.now();
    const html = `
        <div id="${toastId}" class="toast toast-notification show" role="alert">
            <div class="toast-header">
                <strong class="me-auto">🔔 Notificação</strong>
                <button type="button" class="btn-close" data-bs-dismiss="toast"></button>
            </div>
            <div class="toast-body">${message}</div>
        </div>`;
    container.insertAdjacentHTML("beforeend", html);

    const toastEl = document.getElementById(toastId);
    setTimeout(function () {
        toastEl.classList.add("toast-hiding");
        setTimeout(function () { toastEl.remove(); }, 400);
    }, 5000);
}

connection.start().catch(function (err) {
    console.error("SignalR:", err.toString());
});
