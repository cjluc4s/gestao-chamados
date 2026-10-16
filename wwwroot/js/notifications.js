"use strict";

// Notificações em tempo real (SignalR): toast na tela + histórico no sino do menu.
(function () {
    var STORE_KEY = "hd:notifications";
    var MAX_ITEMS = 15;
    var TOAST_MS = 6000;

    var items = load();

    function load() {
        try { return JSON.parse(sessionStorage.getItem(STORE_KEY)) || []; } catch (e) { return []; }
    }

    function save() {
        try { sessionStorage.setItem(STORE_KEY, JSON.stringify(items)); } catch (e) { }
    }

    function timeAgo(ts) {
        var s = Math.max(0, Math.round((Date.now() - ts) / 1000));
        if (s < 45) return "agora mesmo";
        var m = Math.round(s / 60);
        if (m < 60) return "há " + m + " min";
        var h = Math.round(m / 60);
        if (h < 24) return "há " + h + " h";
        return new Date(ts).toLocaleDateString("pt-BR");
    }

    // ----- Sino do menu -----
    var bellBtn, badge, list, empty, clearBtn, unread = 0;

    function renderBell() {
        if (!list) return;
        list.innerHTML = "";
        items.forEach(function (n) {
            var li = document.createElement("li");
            var dot = document.createElement("span");
            dot.className = "dot";
            var box = document.createElement("div");
            var text = document.createElement("span");
            text.textContent = n.message; // texto puro: nunca interpretado como HTML
            var time = document.createElement("time");
            time.textContent = timeAgo(n.at);
            box.appendChild(text);
            box.appendChild(time);
            li.appendChild(dot);
            li.appendChild(box);
            list.appendChild(li);
        });
        empty.hidden = items.length > 0;
        badge.hidden = unread === 0;
        badge.textContent = unread > 9 ? "9+" : String(unread);
        bellBtn.setAttribute("aria-label", unread > 0 ? "Notificações (" + unread + " novas)" : "Notificações");
    }

    function initBell() {
        bellBtn = document.getElementById("notif-bell");
        badge = document.getElementById("notif-count");
        list = document.getElementById("notif-list");
        empty = document.getElementById("notif-empty");
        clearBtn = document.getElementById("notif-clear");
        if (!bellBtn) return;

        bellBtn.addEventListener("show.bs.dropdown", function () {
            unread = 0;
            renderBell();
        });
        clearBtn.addEventListener("click", function () {
            items = [];
            unread = 0;
            save();
            renderBell();
        });
        renderBell();
    }

    // ----- Toast -----
    function showToast(message) {
        var container = document.getElementById("toast-container");
        if (!container) return;

        var toast = document.createElement("div");
        toast.className = "app-toast";
        toast.setAttribute("role", "status");
        toast.style.setProperty("--toast-ms", TOAST_MS + "ms");

        var ico = document.createElement("span");
        ico.className = "toast-ico";
        ico.innerHTML = '<svg class="icon icon-lg" aria-hidden="true"><use href="#i-bell"></use></svg>';

        var body = document.createElement("div");
        var title = document.createElement("span");
        title.className = "toast-title";
        title.textContent = "Nova notificação";
        var text = document.createElement("span");
        text.className = "toast-text";
        text.textContent = message; // texto puro: nunca interpretado como HTML
        body.appendChild(title);
        body.appendChild(text);

        var close = document.createElement("button");
        close.type = "button";
        close.className = "btn-close";
        close.setAttribute("aria-label", "Fechar");

        var bar = document.createElement("span");
        bar.className = "toast-progress";

        toast.appendChild(ico);
        toast.appendChild(body);
        toast.appendChild(close);
        toast.appendChild(bar);
        container.appendChild(toast);

        function dismiss() {
            clearTimeout(timer);
            toast.classList.add("is-leaving");
            setTimeout(function () { toast.remove(); }, 380);
        }

        // O fechamento é por temporizador (e não pelo fim da animação da barra): com "reduzir movimento"
        // ativado no sistema as animações são zeradas e o aviso sumiria na hora.
        // O mouse em cima pausa a contagem.
        var remaining = TOAST_MS;
        var startedAt = Date.now();
        var timer = setTimeout(dismiss, remaining);

        toast.addEventListener("mouseenter", function () {
            clearTimeout(timer);
            remaining -= Date.now() - startedAt;
        });
        toast.addEventListener("mouseleave", function () {
            startedAt = Date.now();
            timer = setTimeout(dismiss, Math.max(remaining, 1500));
        });

        close.addEventListener("click", dismiss);
        // Limita a pilha: descarta os mais antigos.
        while (container.children.length > 4) container.firstElementChild.remove();
    }

    function onNotification(message) {
        items.unshift({ message: String(message), at: Date.now() });
        items = items.slice(0, MAX_ITEMS);
        unread++;
        save();
        renderBell();
        if (badge) {
            badge.classList.remove("pop");
            void badge.offsetWidth; // reinicia a animação
            badge.classList.add("pop");
        }
        showToast(message);
    }

    document.addEventListener("DOMContentLoaded", initBell);

    if (typeof signalR === "undefined") return;

    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/notifications")
        .withAutomaticReconnect()
        .build();

    connection.on("ReceiveNotification", onNotification);

    function emit(state) {
        document.dispatchEvent(new CustomEvent("hd:status", { detail: state }));
    }

    connection.onreconnecting(function () { emit("reconnecting"); });
    connection.onreconnected(function () { emit("live"); });
    connection.onclose(function () { emit("offline"); });

    var started = connection.start()
        .then(function () { emit("live"); return true; })
        .catch(function (err) {
            console.error("SignalR:", err.toString());
            emit("offline");
            return false;
        });

    // Conexão compartilhada com live.js (tempo real nas telas).
    window.hd = {
        connection: connection,
        started: started,
        userId: document.body.getAttribute("data-user-id")
    };
})();
