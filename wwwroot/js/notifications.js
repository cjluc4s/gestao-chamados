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

    const toastEl = document.createElement("div");
    toastEl.className = "toast toast-notification show";
    toastEl.setAttribute("role", "alert");

    const header = document.createElement("div");
    header.className = "toast-header";

    const title = document.createElement("strong");
    title.className = "me-auto";
    title.textContent = "🔔 Notificação";

    const closeBtn = document.createElement("button");
    closeBtn.type = "button";
    closeBtn.className = "btn-close";
    closeBtn.setAttribute("data-bs-dismiss", "toast");

    header.appendChild(title);
    header.appendChild(closeBtn);

    const body = document.createElement("div");
    body.className = "toast-body";
    body.textContent = message; // texto puro: nunca interpretado como HTML

    toastEl.appendChild(header);
    toastEl.appendChild(body);
    container.appendChild(toastEl);

    setTimeout(function () {
        toastEl.classList.add("toast-hiding");
        setTimeout(function () { toastEl.remove(); }, 400);
    }, 5000);
}

connection.start().catch(function (err) {
    console.error("SignalR:", err.toString());
});
