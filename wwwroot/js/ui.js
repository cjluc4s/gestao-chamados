"use strict";

// Interações de interface: tema, animações, formulários e tabelas.
// Tudo é progressivo: sem JavaScript as páginas continuam funcionando.
(function () {
    var reduceMotion = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    function ready(fn) {
        if (document.readyState !== "loading") fn();
        else document.addEventListener("DOMContentLoaded", fn);
    }

    function icon(name) {
        return '<svg class="icon" aria-hidden="true"><use href="#i-' + name + '"></use></svg>';
    }

    // ---------- Tema claro/escuro ----------
    function initTheme() {
        var btn = document.getElementById("theme-toggle");
        if (!btn) return;
        btn.addEventListener("click", function () {
            var root = document.documentElement;
            var next = root.getAttribute("data-bs-theme") === "dark" ? "light" : "dark";
            root.setAttribute("data-bs-theme", next);
            try { localStorage.setItem("theme", next); } catch (e) { }
        });

        // Sem escolha manual, acompanha o tema do sistema em tempo real.
        if (window.matchMedia) {
            window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", function (e) {
                var saved = null;
                try { saved = localStorage.getItem("theme"); } catch (err) { }
                if (!saved) document.documentElement.setAttribute("data-bs-theme", e.matches ? "dark" : "light");
            });
        }
    }

    // ---------- Animação de entrada ao rolar ----------
    function initReveal() {
        var items = document.querySelectorAll(".reveal");
        if (!items.length) return;
        if (reduceMotion || !("IntersectionObserver" in window)) {
            items.forEach(function (el) { el.classList.add("is-visible"); });
            return;
        }
        var io = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    entry.target.classList.add("is-visible");
                    io.unobserve(entry.target);
                }
            });
        }, { threshold: 0.08 });
        items.forEach(function (el, i) {
            if (!el.style.getPropertyValue("--d")) el.style.setProperty("--d", Math.min(i, 8) * 60 + "ms");
            io.observe(el);
        });
    }

    // ---------- Contagem animada nos indicadores ----------
    // animate = false aplica o valor direto (usado nas atualizações ao vivo, para não "piscar").
    function initCountUp(root, animate) {
        (root || document).querySelectorAll("[data-countup]").forEach(function (el) {
            var target = parseInt(el.getAttribute("data-countup"), 10);
            if (isNaN(target)) return;
            if (animate === false || reduceMotion || target === 0) { el.textContent = target; return; }
            var start = null, duration = 900;
            function step(ts) {
                if (start === null) start = ts;
                var p = Math.min((ts - start) / duration, 1);
                el.textContent = Math.round(target * (1 - Math.pow(1 - p, 3)));
                if (p < 1) requestAnimationFrame(step);
            }
            el.textContent = "0";
            requestAnimationFrame(step);
        });
    }

    // ---------- Barras e anel de progresso (dashboard) ----------
    function initMeters(root) {
        requestAnimationFrame(function () {
            var scope = root || document;
            scope.querySelectorAll("[data-bar]").forEach(function (el) {
                el.style.width = el.getAttribute("data-bar") + "%";
            });
            scope.querySelectorAll("[data-donut]").forEach(function (el) {
                var pct = parseFloat(el.getAttribute("data-donut")) || 0;
                var r = parseFloat(el.getAttribute("r"));
                var c = 2 * Math.PI * r;
                el.style.strokeDasharray = (c * pct / 100) + " " + c;
                if (pct <= 0) el.style.strokeLinecap = "butt"; // sem "pontinho" quando está em 0%
            });
        });
    }

    // ---------- Alertas de confirmação ----------
    function initAlerts() {
        document.querySelectorAll("[data-autodismiss]").forEach(function (el) {
            var ms = parseInt(el.getAttribute("data-autodismiss"), 10) || 6000;
            var timer = setTimeout(dismiss, ms);
            el.addEventListener("mouseenter", function () { clearTimeout(timer); });
            function dismiss() {
                el.classList.add("is-leaving");
                setTimeout(function () { el.remove(); }, 320);
            }
            var close = el.querySelector("[data-dismiss-alert]");
            if (close) close.addEventListener("click", function () { clearTimeout(timer); dismiss(); });
        });
    }

    // ---------- Confirmação em modal (substitui o confirm() do navegador) ----------
    // Uso: <form data-confirm="Texto" data-confirm-title="Título" data-confirm-ok="Rótulo do botão">
    function initConfirm() {
        var modalEl = null, modal = null, pendingForm = null, pendingSubmitter = null;

        function build() {
            modalEl = document.createElement("div");
            modalEl.className = "modal fade";
            modalEl.tabIndex = -1;
            modalEl.setAttribute("aria-labelledby", "confirm-title");
            modalEl.setAttribute("aria-hidden", "true");
            modalEl.innerHTML =
                '<div class="modal-dialog modal-dialog-centered"><div class="modal-content"><div class="confirm-body">' +
                '<span class="state-icon is-danger">' + icon("alert") + '</span>' +
                '<h2 id="confirm-title"></h2><p class="text-muted" id="confirm-text"></p>' +
                '<div class="d-flex gap-2 justify-content-center">' +
                '<button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Voltar</button>' +
                '<button type="button" class="btn btn-danger" id="confirm-ok"></button>' +
                '</div></div></div></div>';
            document.body.appendChild(modalEl);
            modal = new bootstrap.Modal(modalEl);
            modalEl.querySelector("#confirm-ok").addEventListener("click", function () {
                var form = pendingForm, submitter = pendingSubmitter;
                modal.hide();
                if (!form) return;
                form.dataset.confirmed = "1";
                if (form.requestSubmit) form.requestSubmit(submitter || undefined);
                else form.submit();
            });
            modalEl.addEventListener("hidden.bs.modal", function () { pendingForm = null; pendingSubmitter = null; });
        }

        // Fase de captura: roda antes da validação e da proteção de duplo envio.
        document.addEventListener("submit", function (e) {
            var form = e.target;
            var text = form.getAttribute && form.getAttribute("data-confirm");
            if (!text || form.dataset.confirmed === "1" || typeof bootstrap === "undefined") return;
            e.preventDefault();
            if (!modal) build();
            pendingForm = form;
            pendingSubmitter = e.submitter || null;
            modalEl.querySelector("#confirm-title").textContent = form.getAttribute("data-confirm-title") || "Confirmar ação";
            modalEl.querySelector("#confirm-text").textContent = text;
            modalEl.querySelector("#confirm-ok").textContent = form.getAttribute("data-confirm-ok") || "Confirmar";
            modal.show();
        }, true);
    }

    // ---------- Botão "carregando" e proteção contra duplo envio ----------
    function initSubmitGuard() {
        document.addEventListener("submit", function (e) {
            var form = e.target;
            // Se a validação do navegador/jQuery barrou o envio, não faz nada.
            if (e.defaultPrevented || form.hasAttribute("data-no-guard")) return;
            if (form.dataset.submitting === "1") { e.preventDefault(); return; }
            form.dataset.submitting = "1";
            var btn = e.submitter || form.querySelector("button[type=submit]");
            if (btn && !btn.classList.contains("dropdown-item")) {
                btn.classList.add("is-loading");
                btn.setAttribute("aria-busy", "true");
                var sp = document.createElement("span");
                sp.className = "spinner";
                sp.setAttribute("aria-hidden", "true");
                btn.prepend(sp);
            }
        });
        // Voltar pelo histórico (cache) não pode deixar o botão travado.
        window.addEventListener("pageshow", function (e) {
            if (!e.persisted) return;
            document.querySelectorAll("form[data-submitting]").forEach(function (f) { delete f.dataset.submitting; });
            document.querySelectorAll(".btn.is-loading").forEach(function (b) {
                b.classList.remove("is-loading");
                b.removeAttribute("aria-busy");
                var sp = b.querySelector(".spinner");
                if (sp) sp.remove();
            });
        });
    }

    // ---------- Contador de caracteres ----------
    function initCounters() {
        document.querySelectorAll("textarea[maxlength], input[maxlength][data-counter]").forEach(function (field) {
            var max = parseInt(field.getAttribute("maxlength"), 10);
            if (!max) return;
            var counter = document.createElement("span");
            counter.className = "field-counter";
            counter.setAttribute("aria-hidden", "true");
            field.insertAdjacentElement("afterend", counter);
            function update() {
                var n = field.value.length;
                counter.textContent = n + " / " + max;
                counter.classList.toggle("is-near", n >= max * 0.9 && n < max);
                counter.classList.toggle("is-over", n >= max);
            }
            field.addEventListener("input", update);
            update();
        });
    }

    // ---------- Campo de senha com mostrar/ocultar ----------
    function initPasswordToggles() {
        document.querySelectorAll("input[type=password]").forEach(function (input) {
            if (input.closest(".password-toggle")) return;
            var wrap = document.createElement("div");
            wrap.className = "password-toggle";
            input.parentNode.insertBefore(wrap, input);
            wrap.appendChild(input);
            var btn = document.createElement("button");
            btn.type = "button";
            btn.className = "toggle-btn";
            btn.setAttribute("aria-label", "Mostrar senha");
            btn.innerHTML = icon("eye");
            btn.addEventListener("click", function () {
                var show = input.type === "password";
                input.type = show ? "text" : "password";
                btn.setAttribute("aria-label", show ? "Ocultar senha" : "Mostrar senha");
                btn.innerHTML = icon(show ? "eye-off" : "eye");
            });
            wrap.appendChild(btn);
        });
    }

    // ---------- Upload: arrastar e soltar, lista e validação ----------
    var ALLOWED = ["jpg", "jpeg", "png", "gif", "pdf", "doc", "docx", "xls", "xlsx", "txt", "zip", "rar"];

    function formatSize(bytes) {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + " KB";
        return (bytes / (1024 * 1024)).toFixed(1) + " MB";
    }

    function initDropzones() {
        document.querySelectorAll("[data-dropzone]").forEach(function (zone) {
            var input = zone.querySelector("input[type=file]");
            var list = document.querySelector(zone.getAttribute("data-dropzone"));
            var maxBytes = (parseInt(zone.getAttribute("data-max-mb"), 10) || 10) * 1024 * 1024;
            if (!input || !list) return;

            function render() {
                list.innerHTML = "";
                Array.prototype.forEach.call(input.files, function (file) {
                    var ext = (file.name.split(".").pop() || "").toLowerCase();
                    var problem = null;
                    if (ALLOWED.indexOf(ext) === -1) problem = "formato não permitido";
                    else if (file.size > maxBytes) problem = "acima de " + (maxBytes / 1048576) + " MB";
                    else if (file.size === 0) problem = "arquivo vazio";

                    var li = document.createElement("li");
                    if (problem) li.className = "is-invalid";
                    var name = document.createElement("span");
                    name.className = "file-name";
                    name.textContent = file.name; // texto puro
                    var size = document.createElement("span");
                    size.className = "file-size";
                    size.textContent = problem ? problem + " — será ignorado" : formatSize(file.size);
                    li.appendChild(name);
                    li.appendChild(size);
                    list.appendChild(li);
                });
            }

            input.addEventListener("change", render);
            ["dragenter", "dragover"].forEach(function (ev) {
                zone.addEventListener(ev, function () { zone.classList.add("is-dragover"); });
            });
            ["dragleave", "drop"].forEach(function (ev) {
                zone.addEventListener(ev, function () { zone.classList.remove("is-dragover"); });
            });
        });
    }

    // ---------- Avaliação por estrelas: mostra o texto da nota escolhida ----------
    // Delegado no documento: o cartão pode ser trocado ao vivo (live.js).
    function initRating() {
        document.addEventListener("change", function (e) {
            var input = e.target;
            if (!input.matches || !input.matches('.rating-stars input[type="radio"]')) return;
            var caption = input.closest("fieldset").querySelector("[data-rating-caption]");
            if (caption) caption.textContent = input.value + " de 5 · " + input.getAttribute("data-label");
        });
    }

    // ---------- Linhas clicáveis ----------
    // Delegado no documento: continua valendo quando a lista é trocada ao vivo (live.js).
    function initClickableRows() {
        document.addEventListener("click", function (e) {
            var row = e.target.closest && e.target.closest("tr[data-href]");
            if (!row) return;
            if (e.target.closest("a, button, input, select, textarea, label")) return;
            if (window.getSelection && String(window.getSelection())) return;
            window.location.href = row.getAttribute("data-href");
        });
    }

    // ---------- Busca instantânea em tabelas ----------
    function initTableFilter() {
        document.querySelectorAll("[data-filter-table]").forEach(function (input) {
            var table = document.querySelector(input.getAttribute("data-filter-table"));
            if (!table) return;
            var rows = table.querySelectorAll("tbody tr:not(.empty-row)");
            var empty = table.querySelector("tr.empty-row");
            var counter = document.querySelector(input.getAttribute("data-filter-count") || "");

            input.addEventListener("input", function () {
                var q = input.value.trim().toLowerCase();
                var visible = 0;
                rows.forEach(function (row) {
                    var match = !q || row.textContent.toLowerCase().indexOf(q) !== -1;
                    row.hidden = !match;
                    if (match) visible++;
                });
                if (empty) empty.hidden = visible !== 0;
                if (counter) counter.textContent = visible + (visible === 1 ? " usuário" : " usuários");
            });
        });
    }

    ready(function () {
        initTheme();
        initAlerts();
        initConfirm();
        initSubmitGuard();
        initCounters();
        initPasswordToggles();
        initDropzones();
        initClickableRows();
        initRating();
        initTableFilter();
        initReveal();
        initCountUp();
        initMeters();
    });

    // Usado por live.js depois de trocar trechos da página.
    window.hdUi = { countUp: initCountUp, meters: initMeters };
})();
