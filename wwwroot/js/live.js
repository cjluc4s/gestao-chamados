"use strict";

// Tempo real nas telas: detalhes do chamado (atualização automática, presença e "digitando…"),
// lista de chamados (aviso de novidades) e dashboard (números ao vivo).
// A fonte da verdade continua sendo o servidor: ao receber um aviso, a página busca a si mesma
// e troca só os trechos marcados com data-live-region, respeitando as permissões de quem está vendo.
(function () {
    var hd = window.hd;
    if (!hd) return;

    var conn = hd.connection;
    var detailsRoot = document.querySelector("[data-live-ticket]");
    var listRoot = document.querySelector("[data-live-list]");
    var dashRoot = document.querySelector("[data-live-dashboard]");
    if (!detailsRoot && !listRoot && !dashRoot) return;

    var ticketId = detailsRoot ? parseInt(detailsRoot.getAttribute("data-live-ticket"), 10) : null;

    // ---------- Indicador "Ao vivo" ----------
    var LABELS = { connecting: "Conectando…", live: "Ao vivo", reconnecting: "Reconectando…", offline: "Sem conexão" };

    function setStatus(state) {
        document.querySelectorAll("[data-live-status]").forEach(function (el) {
            el.setAttribute("data-state", state);
            var label = el.querySelector(".label");
            if (label) label.textContent = LABELS[state] || "";
        });
    }

    setStatus("connecting");
    document.addEventListener("hd:status", function (e) {
        setStatus(e.detail);
        // Depois de uma queda, o que aconteceu enquanto estava fora do ar precisa ser buscado.
        if (e.detail === "live" && joined) {
            joinTicket();
            scheduleRefresh(0);
        }
    });

    // ---------- Troca de trechos ----------
    var inflight = false;
    var queued = false;
    var timer = null;
    var deferred = false;

    function signature(node) {
        // Cada renderização gera um token antifalsificação novo; ele não conta como mudança.
        var copy = node.cloneNode(true);
        copy.querySelectorAll('input[name="__RequestVerificationToken"]').forEach(function (i) { i.remove(); });
        return copy.innerHTML.replace(/\s+/g, " ").trim();
    }

    function isEditing(el) {
        var active = document.activeElement;
        return active && el.contains(active) && /^(INPUT|TEXTAREA|SELECT)$/.test(active.tagName);
    }

    function apply(doc) {
        var changed = false;
        document.querySelectorAll("[data-live-region]").forEach(function (el) {
            var name = el.getAttribute("data-live-region");
            var next = doc.querySelector('[data-live-region="' + name + '"]');
            if (!next || signature(el) === signature(next)) return;

            if (isEditing(el)) { // não atropela quem está escolhendo algo
                deferred = true;
                return;
            }

            el.innerHTML = next.innerHTML;
            el.querySelectorAll(".reveal").forEach(function (r) { r.classList.add("is-visible"); });
            if (window.hdUi) {
                window.hdUi.countUp(el, false);
                window.hdUi.meters(el);
            }

            el.classList.remove("live-flash");
            void el.offsetWidth; // reinicia a animação
            el.classList.add("live-flash");
            changed = true;
        });
        return changed;
    }

    function refresh() {
        if (inflight) { queued = true; return; }
        inflight = true;

        fetch(location.pathname + location.search, { credentials: "same-origin", headers: { "X-Requested-With": "live" } })
            .then(function (r) {
                if (r.redirected && /\/Identity\/Account\//i.test(r.url)) { location.reload(); throw "redirect"; }
                if (r.status === 403 || r.status === 404) { throw "gone"; }
                if (!r.ok) { throw new Error("HTTP " + r.status); }
                return r.text();
            })
            .then(function (html) { apply(new DOMParser().parseFromString(html, "text/html")); })
            .catch(function (e) {
                if (e === "gone") {
                    // O chamado foi reatribuído ou cancelado e esta pessoa perdeu o acesso.
                    var notice = document.getElementById("live-gone");
                    if (notice) notice.hidden = false;
                } else if (typeof e !== "string") {
                    console.warn("Atualização ao vivo falhou:", e);
                }
            })
            .then(function () {
                inflight = false;
                if (queued) { queued = false; refresh(); }
            });
    }

    function scheduleRefresh(delay) {
        clearTimeout(timer);
        timer = setTimeout(refresh, delay);
    }

    // Se algo ficou pendente porque o foco estava num campo, atualiza quando ele sair.
    document.addEventListener("focusout", function () {
        if (deferred) {
            deferred = false;
            scheduleRefresh(150);
        }
    });

    // ---------- Eventos do servidor ----------
    conn.on("TicketChanged", function (e) {
        if (!e || e.byUserId === hd.userId) return; // a própria ação já recarregou a página

        if (detailsRoot) {
            if (e.ticketId === ticketId) {
                if (e.kind === "comment") hideTyping();
                scheduleRefresh(300);
            }
        } else if (listRoot) {
            var pill = document.getElementById("live-list-pill");
            if (pill) pill.hidden = false;
        } else if (dashRoot) {
            scheduleRefresh(800);
        }
    });

    var listPill = document.getElementById("live-list-pill");
    if (listPill) {
        listPill.addEventListener("click", function () {
            listPill.hidden = true;
            refresh();
        });
    }

    // ---------- Presença e "digitando…" (só nos detalhes) ----------
    var joined = false;
    var presenceEl = document.getElementById("presence");
    var typingEl = document.getElementById("typing");
    var typingTimer = null;

    function initials(name) {
        var parts = String(name).trim().split(/\s+/).filter(Boolean);
        if (!parts.length) return "?";
        return (parts.length > 1 ? parts[0][0] + parts[parts.length - 1][0] : parts[0].slice(0, 2)).toUpperCase();
    }

    function renderPresence(viewers) {
        if (!presenceEl) return;
        var others = (viewers || []).filter(function (v) { return v.userId !== hd.userId; });
        presenceEl.innerHTML = "";
        presenceEl.hidden = others.length === 0;
        if (!others.length) return;

        var stack = document.createElement("span");
        stack.className = "presence-stack";
        others.slice(0, 4).forEach(function (v) {
            var a = document.createElement("span");
            a.className = "avatar avatar-sm";
            a.title = v.name;
            a.textContent = initials(v.name);
            stack.appendChild(a);
        });

        var text = document.createElement("span");
        text.className = "presence-text";
        text.textContent = others.length === 1
            ? others[0].name + " também está vendo"
            : others.length + " pessoas também estão vendo";

        presenceEl.appendChild(stack);
        presenceEl.appendChild(text);
    }

    function hideTyping() {
        if (typingEl) typingEl.hidden = true;
        clearTimeout(typingTimer);
    }

    function joinTicket() {
        return conn.invoke("ViewTicket", ticketId).catch(function (err) {
            console.warn("Presença indisponível:", err && err.message);
        });
    }

    if (detailsRoot) {
        hd.started.then(function (ok) {
            if (!ok) return;
            joined = true;
            joinTicket();
        });

        conn.on("PresenceChanged", function (id, viewers) {
            if (id === ticketId) renderPresence(viewers);
        });

        conn.on("UserTyping", function (id, name) {
            if (id !== ticketId || !typingEl) return;
            typingEl.querySelector(".typing-name").textContent = name;
            typingEl.hidden = false;
            clearTimeout(typingTimer);
            typingTimer = setTimeout(hideTyping, 3500);
        });

        var message = document.getElementById("comment-message");
        var lastTyping = 0;
        if (message) {
            message.addEventListener("input", function () {
                var now = Date.now();
                if (now - lastTyping < 2000 || !message.value.trim()) return;
                lastTyping = now;
                if (conn.state === "Connected") conn.invoke("Typing", ticketId).catch(function () { });
            });
        }

        window.addEventListener("pagehide", function () {
            if (conn.state === "Connected") conn.invoke("LeaveTicket", ticketId).catch(function () { });
        });
    }
})();
