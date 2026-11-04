/*!
 * Jellyfin Profile Picker — client web
 * Écran « Qui regarde ? » par-dessus la page de connexion de jellyfin-web,
 * réutilisant le formulaire natif pour l'authentification (aucun mot de passe
 * ne transite par le plugin).
 */
(function () {
    'use strict';

    if (window.__profilePickerLoaded) {
        return;
    }
    window.__profilePickerLoaded = true;

    // ------------------------------------------------------------------ //
    // Contexte
    // ------------------------------------------------------------------ //
    var SCRIPT = document.currentScript;
    var BASE = (function () {
        var src = (SCRIPT && SCRIPT.src) || '';
        var i = src.indexOf('/ProfilePicker/');
        if (i > 0) {
            return src.slice(0, i);
        }
        var p = location.pathname;
        var w = p.indexOf('/web/');
        return location.origin + (w >= 0 ? p.slice(0, w) : '');
    })();

    var DEFAULTS = {
        enabled: true,
        theme: 'cinema',
        title: 'Qui regarde ?',
        accentColor: '#e50914',
        pinMode: false,
        pinLength: 4,
        showManualLogin: true,
        showQuickConnect: true,
        rememberMe: true,
        allowUserAvatarChoice: true
    };

    var CFG = null;
    var overlay = null;
    var dismissedPage = null;
    var pending = null;
    var modal = null;
    var serverName = '';
    var checkTimer = null;

    function log() {
        var a = Array.prototype.slice.call(arguments);
        a.unshift('[ProfilePicker]');
        console.debug.apply(console, a);
    }

    function el(tag, cls, text) {
        var e = document.createElement(tag);
        if (cls) {
            e.className = cls;
        }
        if (text != null) {
            e.textContent = text;
        }
        return e;
    }

    function getJSON(url) {
        return fetch(url, { credentials: 'same-origin', headers: { Accept: 'application/json' } }).then(function (r) {
            if (!r.ok) {
                throw new Error('HTTP ' + r.status);
            }
            return r.json();
        });
    }

    function hashColor(s) {
        var h = 0;
        for (var i = 0; i < s.length; i++) {
            h = (h * 31 + s.charCodeAt(i)) >>> 0;
        }
        return 'hsl(' + (h % 360) + ', 62%, 46%)';
    }

    function isVisible(node) {
        return !!node && !node.classList.contains('hide') && node.getClientRects().length > 0;
    }

    function activePage(id) {
        var pages = document.querySelectorAll('#' + id);
        for (var i = 0; i < pages.length; i++) {
            if (isVisible(pages[i])) {
                return pages[i];
            }
        }
        return null;
    }

    function userImageUrl(user, size) {
        if (!user.PrimaryImageTag) {
            return null;
        }
        return BASE + '/UserImage?userId=' + encodeURIComponent(user.Id)
            + '&tag=' + encodeURIComponent(user.PrimaryImageTag)
            + '&quality=90&maxHeight=' + (size || 360);
    }

    function legacyUserImageUrl(user, size) {
        return BASE + '/Users/' + encodeURIComponent(user.Id) + '/Images/Primary?tag='
            + encodeURIComponent(user.PrimaryImageTag || '') + '&quality=90&maxHeight=' + (size || 360);
    }

    function avatarNode(user, size) {
        var box = el('div', 'pp-avatar');
        var url = userImageUrl(user, size);
        var fallback = function () {
            box.innerHTML = '';
            box.classList.add('pp-avatar-initial');
            box.style.background = hashColor(user.Name || '?');
            box.appendChild(el('span', null, (user.Name || '?').trim().charAt(0).toUpperCase()));
        };
        if (!url) {
            fallback();
            return box;
        }
        var img = el('img');
        img.alt = '';
        img.loading = 'lazy';
        img.decoding = 'async';
        img.draggable = false;
        var triedLegacy = false;
        img.onerror = function () {
            if (!triedLegacy) {
                triedLegacy = true;
                img.src = legacyUserImageUrl(user, size);
            } else {
                fallback();
            }
        };
        img.src = url;
        box.appendChild(img);
        return box;
    }

    function lockIcon() {
        var s = el('span', 'pp-lock');
        s.setAttribute('aria-label', 'Protégé par mot de passe');
        s.innerHTML = '<svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path fill="currentColor" d="M12 1a5 5 0 0 0-5 5v3H6a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V11a2 2 0 0 0-2-2h-1V6a5 5 0 0 0-5-5Zm-3 8V6a3 3 0 1 1 6 0v3H9Z"/></svg>';
        return s;
    }

    function applyTheme(node) {
        node.classList.add('pp-root', 'pp-theme-' + (CFG.theme === 'nightblue' ? 'nightblue' : 'cinema'));
        node.style.setProperty('--pp-accent', /^#[0-9a-f]{3,8}$/i.test(CFG.accentColor || '') ? CFG.accentColor : DEFAULTS.accentColor);
    }

    // ------------------------------------------------------------------ //
    // Écran « Qui regarde ? »
    // ------------------------------------------------------------------ //
    function showOverlay(page) {
        overlay = el('div');
        overlay.id = 'pp-overlay';
        overlay.setAttribute('role', 'dialog');
        overlay.setAttribute('aria-modal', 'true');
        applyTheme(overlay);
        overlay.classList.add('pp-loading');

        var header = el('div', 'pp-header');
        header.appendChild(el('div', 'pp-server', serverName));
        overlay.appendChild(header);

        var center = el('div', 'pp-center');
        var title = el('h1', 'pp-title', CFG.title || DEFAULTS.title);
        var grid = el('div', 'pp-grid');
        var actions = el('div', 'pp-actions');
        center.appendChild(title);
        center.appendChild(grid);
        center.appendChild(actions);
        overlay.appendChild(center);
        document.body.appendChild(overlay);
        document.documentElement.classList.add('pp-open');

        getJSON(BASE + '/Users/Public').then(function (users) {
            if (!overlay) {
                return;
            }
            if (!users || !users.length) {
                // Aucun profil visible : on laisse le formulaire natif.
                log('aucun utilisateur public, écran natif conservé');
                dismiss(page);
                return;
            }
            users.forEach(function (u, i) {
                var tile = el('button', 'pp-profile');
                tile.type = 'button';
                tile.style.animationDelay = (i * 45) + 'ms';
                tile.appendChild(avatarNode(u, 360));
                var label = el('span', 'pp-name', u.Name);
                if (u.HasPassword) {
                    label.appendChild(lockIcon());
                }
                tile.appendChild(label);
                tile.addEventListener('click', function () {
                    onProfileClick(u);
                });
                grid.appendChild(tile);
            });

            var quick = page.querySelector('.btnQuick');
            if (CFG.showQuickConnect && quick && !quick.classList.contains('hide')) {
                var q = el('button', 'pp-btn pp-btn-outline', 'Quick Connect');
                q.type = 'button';
                q.addEventListener('click', function () {
                    dismiss(page);
                    quick.click();
                });
                actions.appendChild(q);
            }
            if (CFG.showManualLogin) {
                var m = el('button', 'pp-btn pp-btn-outline', 'Autre utilisateur');
                m.type = 'button';
                m.addEventListener('click', function () {
                    dismiss(page);
                    var b = page.querySelector('.btnManual');
                    if (b) {
                        b.click();
                    }
                });
                actions.appendChild(m);
            }
            overlay.classList.remove('pp-loading');
        }).catch(function (e) {
            log('Users/Public indisponible', e);
            dismiss(page);
        });
    }

    function removeOverlay() {
        closeModal();
        if (overlay) {
            overlay.remove();
            overlay = null;
        }
        document.documentElement.classList.remove('pp-open');
    }

    function dismiss(page) {
        dismissedPage = page;
        removeOverlay();
    }

    function onProfileClick(user) {
        if (pending) {
            return;
        }
        if (!user.HasPassword) {
            doLogin(user, '');
            return;
        }
        openPasswordModal(user);
    }

    // ------------------------------------------------------------------ //
    // Saisie du mot de passe / PIN
    // ------------------------------------------------------------------ //
    function openPasswordModal(user) {
        closeModal();
        modal = el('div', 'pp-modal');
        applyTheme(modal);
        var card = el('div', 'pp-modal-card');
        card.appendChild(avatarNode(user, 240));
        card.appendChild(el('div', 'pp-modal-name', user.Name));
        card.appendChild(el('div', 'pp-modal-hint', CFG.pinMode
            ? 'Saisis ton code PIN pour accéder à ce profil.'
            : 'Saisis ton mot de passe pour accéder à ce profil.'));

        var form = el('form', 'pp-pw-form');
        form.setAttribute('autocomplete', 'on');
        // Champ username caché pour les gestionnaires de mots de passe.
        var hiddenUser = el('input');
        hiddenUser.type = 'text';
        hiddenUser.name = 'username';
        hiddenUser.autocomplete = 'username';
        hiddenUser.value = user.Name;
        hiddenUser.className = 'pp-sr-only';
        hiddenUser.tabIndex = -1;
        form.appendChild(hiddenUser);

        var getValue;
        if (CFG.pinMode) {
            var len = Math.max(4, Math.min(8, parseInt(CFG.pinLength, 10) || 4));
            var pin = el('div', 'pp-pin');
            var boxes = [];
            for (var i = 0; i < len; i++) {
                var d = el('input', 'pp-pin-digit');
                d.type = 'password';
                d.inputMode = 'numeric';
                d.maxLength = 1;
                d.autocomplete = 'off';
                d.setAttribute('aria-label', 'Chiffre ' + (i + 1));
                boxes.push(d);
                pin.appendChild(d);
            }
            boxes.forEach(function (b, idx) {
                b.addEventListener('input', function () {
                    b.value = b.value.replace(/\D/g, '').slice(-1);
                    if (b.value && idx < boxes.length - 1) {
                        boxes[idx + 1].focus();
                    }
                    if (boxes.every(function (x) { return x.value; })) {
                        submit();
                    }
                });
                b.addEventListener('keydown', function (e) {
                    if (e.key === 'Backspace' && !b.value && idx > 0) {
                        boxes[idx - 1].focus();
                        boxes[idx - 1].value = '';
                        e.preventDefault();
                    }
                });
                b.addEventListener('paste', function (e) {
                    var t = ((e.clipboardData || window.clipboardData).getData('text') || '').replace(/\D/g, '');
                    if (!t) {
                        return;
                    }
                    e.preventDefault();
                    for (var k = 0; k < boxes.length; k++) {
                        boxes[k].value = t.charAt(k) || '';
                    }
                    (boxes[Math.min(t.length, boxes.length) - 1] || boxes[0]).focus();
                    if (t.length >= boxes.length) {
                        submit();
                    }
                });
            });
            form.appendChild(pin);
            getValue = function () {
                return boxes.map(function (x) { return x.value; }).join('');
            };
            modal.__focus = function () { boxes[0].focus(); };
            modal.__reset = function () {
                boxes.forEach(function (x) { x.value = ''; });
                boxes[0].focus();
            };
        } else {
            var pw = el('input', 'pp-pw');
            pw.type = 'password';
            pw.name = 'password';
            pw.autocomplete = 'current-password';
            pw.placeholder = 'Mot de passe';
            form.appendChild(pw);
            getValue = function () { return pw.value; };
            modal.__focus = function () { pw.focus(); };
            modal.__reset = function () { pw.value = ''; pw.focus(); };
        }

        var err = el('div', 'pp-error');
        err.setAttribute('role', 'alert');
        form.appendChild(err);
        modal.__error = err;

        var actions = el('div', 'pp-modal-actions');
        var cancel = el('button', 'pp-btn pp-btn-ghost', 'Annuler');
        cancel.type = 'button';
        cancel.addEventListener('click', closeModal);
        var ok = el('button', 'pp-btn pp-btn-primary', 'Continuer');
        ok.type = 'submit';
        actions.appendChild(cancel);
        actions.appendChild(ok);
        form.appendChild(actions);

        function submit() {
            if (pending) {
                return;
            }
            var v = getValue();
            if (CFG.pinMode && v.length < (parseInt(CFG.pinLength, 10) || 4)) {
                return;
            }
            err.textContent = '';
            doLogin(user, v);
        }

        form.addEventListener('submit', function (e) {
            e.preventDefault();
            submit();
        });
        modal.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                closeModal();
            }
        });
        modal.addEventListener('click', function (e) {
            if (e.target === modal) {
                closeModal();
            }
        });

        card.appendChild(form);
        modal.appendChild(card);
        document.body.appendChild(modal);
        setTimeout(function () { modal && modal.__focus && modal.__focus(); }, 30);
    }

    function closeModal() {
        if (modal) {
            modal.remove();
            modal = null;
        }
    }

    // ------------------------------------------------------------------ //
    // Authentification via le formulaire natif de jellyfin-web
    // ------------------------------------------------------------------ //
    function doLogin(user, password) {
        var page = activePage('loginPage');
        var form = page && page.querySelector('.manualLoginForm');
        var name = page && page.querySelector('#txtManualName');
        var pw = page && page.querySelector('#txtManualPassword');
        if (!form || !name || !pw) {
            log('formulaire natif introuvable, repli sur l\'écran natif');
            dismiss(page);
            return;
        }
        name.value = user.Name;
        pw.value = password || '';
        var remember = page.querySelector('.chkRememberLogin');
        if (remember && typeof CFG.rememberMe === 'boolean') {
            remember.checked = CFG.rememberMe;
        }

        pending = { user: user, ts: Date.now() };
        setBusy(true);
        if (typeof form.requestSubmit === 'function') {
            form.requestSubmit();
        } else {
            form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
        }
        var ts = pending.ts;
        setTimeout(function () {
            if (pending && pending.ts === ts) {
                onAuthResult(0);
            }
        }, 20000);
    }

    function setBusy(b) {
        [overlay, modal].forEach(function (n) {
            if (n) {
                n.classList.toggle('pp-busy', b);
            }
        });
    }

    function onAuthResult(status) {
        if (!pending) {
            return;
        }
        var user = pending.user;
        pending = null;
        setBusy(false);
        if (status >= 200 && status < 300) {
            if (overlay) {
                overlay.classList.add('pp-leaving');
            }
            closeModal();
            return;
        }
        var msg = status === 401 ? (CFG.pinMode ? 'Code PIN incorrect.' : 'Mot de passe incorrect.')
            : status === 403 ? 'Ce compte n\'est pas autorisé à se connecter.'
                : 'Connexion au serveur impossible.';
        if (!modal && user.HasPassword) {
            openPasswordModal(user);
        }
        if (modal) {
            modal.__error.textContent = msg;
            modal.classList.remove('pp-shake');
            void modal.offsetWidth;
            modal.classList.add('pp-shake');
            modal.__reset();
        }
    }

    // On observe la réponse d'authentification émise par jellyfin-web.
    (function hookFetch() {
        var orig = window.fetch;
        if (!orig) {
            return;
        }
        window.fetch = function (input) {
            var url = typeof input === 'string' ? input : (input && input.url) || '';
            var p = orig.apply(this, arguments);
            if (pending && /\/Users\/AuthenticateByName/i.test(url)) {
                p.then(function (r) { onAuthResult(r.status); }, function () { onAuthResult(0); });
            }
            return p;
        };
    })();

    // ------------------------------------------------------------------ //
    // Sélecteur d'avatars prédéfinis
    // ------------------------------------------------------------------ //
    function openAvatarPicker(opts) {
        closeModal();
        modal = el('div', 'pp-modal');
        applyTheme(modal);
        var card = el('div', 'pp-modal-card pp-picker');
        card.appendChild(el('h2', 'pp-picker-title', opts.title || 'Choisis un avatar'));
        var body = el('div', 'pp-picker-body');
        body.appendChild(el('div', 'pp-modal-hint', 'Chargement…'));
        card.appendChild(body);
        var actions = el('div', 'pp-modal-actions');
        var cancel = el('button', 'pp-btn pp-btn-ghost', 'Fermer');
        cancel.type = 'button';
        cancel.addEventListener('click', closeModal);
        actions.appendChild(cancel);
        card.appendChild(actions);
        modal.appendChild(card);
        modal.addEventListener('click', function (e) {
            if (e.target === modal) {
                closeModal();
            }
        });
        modal.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                closeModal();
            }
        });
        document.body.appendChild(modal);

        getJSON(BASE + '/ProfilePicker/Avatars').then(function (list) {
            body.innerHTML = '';
            var groups = {};
            var order = [];
            list.forEach(function (a) {
                if (!groups[a.category]) {
                    groups[a.category] = [];
                    order.push(a.category);
                }
                groups[a.category].push(a);
            });
            order.forEach(function (cat) {
                body.appendChild(el('h3', 'pp-picker-cat', cat));
                var g = el('div', 'pp-picker-grid');
                groups[cat].forEach(function (a) {
                    var b = el('button', 'pp-picker-item');
                    b.type = 'button';
                    b.title = a.name;
                    var img = el('img');
                    img.alt = a.name;
                    img.loading = 'lazy';
                    img.src = BASE + '/ProfilePicker/Avatars/' + encodeURIComponent(a.id);
                    b.appendChild(img);
                    b.addEventListener('click', function () {
                        modal && modal.classList.add('pp-busy');
                        Promise.resolve(opts.onPick(a)).then(closeModal, function (e) {
                            modal && modal.classList.remove('pp-busy');
                            alert('Impossible d\'appliquer l\'avatar : ' + (e && e.message ? e.message : e));
                        });
                    });
                    g.appendChild(b);
                });
                body.appendChild(g);
            });
        }).catch(function () {
            body.innerHTML = '';
            body.appendChild(el('div', 'pp-error', 'Impossible de charger les avatars.'));
        });
    }

    function blobToBase64(blob) {
        return new Promise(function (resolve, reject) {
            var r = new FileReader();
            r.onload = function () { resolve(String(r.result).split(',')[1]); };
            r.onerror = reject;
            r.readAsDataURL(blob);
        });
    }

    function applyAvatar(userId, avatarId) {
        return fetch(BASE + '/ProfilePicker/Avatars/' + encodeURIComponent(avatarId)).then(function (r) {
            if (!r.ok) {
                throw new Error('avatar introuvable');
            }
            return r.blob();
        }).then(function (blob) {
            var type = blob.type || 'image/png';
            var api = window.ApiClient;
            if (api && typeof api.uploadUserImage === 'function' && typeof File === 'function') {
                return api.uploadUserImage(userId, 'Primary', new File([blob], 'avatar.png', { type: type }));
            }
            var token = api && api.accessToken && api.accessToken();
            return blobToBase64(blob).then(function (b64) {
                return fetch(BASE + '/UserImage?userId=' + encodeURIComponent(userId), {
                    method: 'POST',
                    headers: { 'Content-Type': type, Authorization: 'MediaBrowser Token="' + token + '"' },
                    body: b64
                });
            }).then(function (r) {
                if (!r.ok) {
                    throw new Error('HTTP ' + r.status);
                }
            });
        });
    }

    // Bouton « Avatar prédéfini » sur la page Profil de l'utilisateur.
    function ensureProfileButton() {
        var page = activePage('userProfilePage');
        if (!page || page.querySelector('.pp-choose-avatar')) {
            return;
        }
        var anchor = page.querySelector('#btnAddImage');
        if (!anchor) {
            return;
        }
        var btn = el('button', anchor.className + ' pp-choose-avatar', 'Avatar prédéfini');
        btn.type = 'button';
        anchor.parentNode.insertBefore(btn, anchor.nextSibling);
        btn.addEventListener('click', function () {
            var q = new URLSearchParams((location.hash.split('?')[1]) || '');
            var userId = q.get('userId') || (window.ApiClient && window.ApiClient.getCurrentUserId());
            openAvatarPicker({
                title: 'Choisis ton avatar',
                onPick: function (a) {
                    return applyAvatar(userId, a.id).then(function () {
                        location.reload();
                    });
                }
            });
        });
    }

    // ------------------------------------------------------------------ //
    // Boucle de détection (SPA : les pages sont montées/démontées)
    // ------------------------------------------------------------------ //
    function check() {
        checkTimer = null;
        if (!CFG || !CFG.enabled) {
            return;
        }
        var page = activePage('loginPage');
        if (page) {
            if (!overlay && dismissedPage !== page && !pending) {
                showOverlay(page);
            }
        } else {
            if (overlay) {
                removeOverlay();
            }
            pending = null;
            dismissedPage = null;
        }
        if (CFG.allowUserAvatarChoice) {
            ensureProfileButton();
        }
    }

    function scheduleCheck() {
        if (!checkTimer) {
            checkTimer = setTimeout(check, 120);
        }
    }

    function start(cfg) {
        CFG = Object.assign({}, DEFAULTS, cfg || {});
        new MutationObserver(scheduleCheck).observe(document.documentElement, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['class']
        });
        window.addEventListener('hashchange', scheduleCheck);
        window.addEventListener('popstate', scheduleCheck);
        scheduleCheck();
    }

    window.ProfilePicker = {
        base: BASE,
        openAvatarPicker: openAvatarPicker,
        applyAvatar: applyAvatar,
        refresh: function () { dismissedPage = null; removeOverlay(); scheduleCheck(); }
    };

    getJSON(BASE + '/System/Info/Public').then(function (i) {
        serverName = (i && i.ServerName) || '';
        var s = overlay && overlay.querySelector('.pp-server');
        if (s) {
            s.textContent = serverName;
        }
    }).catch(function () {});

    getJSON(BASE + '/ProfilePicker/Config').then(start, function (e) {
        log('configuration indisponible, valeurs par défaut', e);
        start(null);
    });
})();
