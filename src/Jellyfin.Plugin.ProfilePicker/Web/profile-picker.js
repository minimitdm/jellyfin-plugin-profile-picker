/*!
 * Jellyfin Profile Picker — client web
 *
 * Mode « household » (défaut) : connexion classique avec le compte du foyer,
 *   puis écran « Qui regarde ? » listant les profils du foyer. Le choix d'un
 *   profil bascule la session côté serveur (/ProfilePicker/Profiles/{id}/Switch).
 * Mode « public » : grille des utilisateurs publics directement sur la page de
 *   connexion, authentification via le formulaire natif.
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
        mode: 'household',
        alwaysAskProfile: true,
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

    var CHOSEN_KEY = 'pp_chosen';
    var CFG = null;
    var overlay = null;          // écran de sélection courant (dataset.kind = public | household)
    var dismissedPage = null;    // mode public : page de login sur laquelle l'utilisateur a fermé l'écran
    var pending = null;          // mode public : connexion en cours via le formulaire natif
    var modal = null;
    var serverName = '';
    var checkTimer = null;
    var hh = { userId: null, data: null, loading: false };

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

    function norm(id) {
        return (id || '').toString().replace(/-/g, '').toLowerCase();
    }

    function getJSON(url) {
        return fetch(url, { credentials: 'same-origin', headers: { Accept: 'application/json' } }).then(function (r) {
            if (!r.ok) {
                throw new Error('HTTP ' + r.status);
            }
            return r.json();
        });
    }

    // Appel authentifié avec l'en-tête complet de jellyfin-web (Client, Device, DeviceId, Version, Token).
    function apiCall(method, path, body) {
        var api = window.ApiClient;
        if (!api || typeof api.ajax !== 'function') {
            return Promise.reject({ status: 0 });
        }
        var req = { type: method, url: api.getUrl(path), dataType: 'json' };
        if (body !== undefined) {
            req.data = JSON.stringify(body);
            req.contentType = 'application/json';
        }
        return api.ajax(req);
    }

    function readError(resp) {
        if (resp && typeof resp.json === 'function') {
            return resp.json().then(function (j) { return { status: resp.status, body: j }; }, function () { return { status: resp.status, body: {} }; });
        }
        return Promise.resolve({ status: (resp && resp.status) || 0, body: {} });
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

    function avatarNode(user, size) {
        var box = el('div', 'pp-avatar');
        var fallback = function () {
            box.innerHTML = '';
            box.classList.add('pp-avatar-initial');
            box.style.background = hashColor(user.Name || '?');
            box.appendChild(el('span', null, (user.Name || '?').trim().charAt(0).toUpperCase()));
        };
        if (!user.PrimaryImageTag) {
            fallback();
            return box;
        }
        var q = '&tag=' + encodeURIComponent(user.PrimaryImageTag) + '&quality=90&maxHeight=' + (size || 360);
        var img = el('img');
        img.alt = '';
        img.decoding = 'async';
        img.draggable = false;
        var triedLegacy = false;
        img.onerror = function () {
            if (!triedLegacy) {
                triedLegacy = true;
                img.src = BASE + '/Users/' + encodeURIComponent(user.Id) + '/Images/Primary?' + q.slice(1);
            } else {
                fallback();
            }
        };
        img.src = BASE + '/UserImage?userId=' + encodeURIComponent(user.Id) + q;
        box.appendChild(img);
        return box;
    }

    function lockIcon() {
        var s = el('span', 'pp-lock');
        s.setAttribute('aria-label', 'Profil verrouillé');
        s.innerHTML = '<svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path fill="currentColor" d="M12 1a5 5 0 0 0-5 5v3H6a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V11a2 2 0 0 0-2-2h-1V6a5 5 0 0 0-5-5Zm-3 8V6a3 3 0 1 1 6 0v3H9Z"/></svg>';
        return s;
    }

    function applyTheme(node) {
        node.classList.add('pp-root', 'pp-theme-' + (CFG.theme === 'nightblue' ? 'nightblue' : 'cinema'));
        node.style.setProperty('--pp-accent', /^#[0-9a-f]{3,8}$/i.test(CFG.accentColor || '') ? CFG.accentColor : DEFAULTS.accentColor);
    }

    // Squelette commun de l'écran de sélection.
    function buildOverlay(kind, headerText) {
        removeOverlay();
        overlay = el('div');
        overlay.id = 'pp-overlay';
        overlay.dataset.kind = kind;
        overlay.setAttribute('role', 'dialog');
        overlay.setAttribute('aria-modal', 'true');
        applyTheme(overlay);
        overlay.classList.add('pp-loading');

        var header = el('div', 'pp-header');
        header.appendChild(el('div', 'pp-server', headerText || serverName));
        overlay.appendChild(header);

        var center = el('div', 'pp-center');
        center.appendChild(el('h1', 'pp-title', CFG.title || DEFAULTS.title));
        var grid = el('div', 'pp-grid');
        var actions = el('div', 'pp-actions');
        center.appendChild(grid);
        center.appendChild(actions);
        overlay.appendChild(center);
        document.body.appendChild(overlay);
        document.documentElement.classList.add('pp-open');
        return { grid: grid, actions: actions };
    }

    function addTile(grid, user, i, onClick, current) {
        var tile = el('button', 'pp-profile' + (current ? ' pp-current' : ''));
        tile.type = 'button';
        tile.style.animationDelay = (i * 45) + 'ms';
        tile.appendChild(avatarNode(user, 360));
        var label = el('span', 'pp-name', user.Name);
        if (user.locked) {
            label.appendChild(lockIcon());
        }
        tile.appendChild(label);
        tile.addEventListener('click', onClick);
        grid.appendChild(tile);
    }

    function addAction(actions, text, onClick) {
        var b = el('button', 'pp-btn pp-btn-outline', text);
        b.type = 'button';
        b.addEventListener('click', onClick);
        actions.appendChild(b);
    }

    function removeOverlay() {
        closeModal();
        if (overlay) {
            overlay.remove();
            overlay = null;
        }
        document.documentElement.classList.remove('pp-open');
    }

    // ------------------------------------------------------------------ //
    // Fenêtre de saisie (PIN ou mot de passe)
    // ------------------------------------------------------------------ //
    function openSecretModal(user, opts) {
        closeModal();
        modal = el('div', 'pp-modal');
        applyTheme(modal);
        var card = el('div', 'pp-modal-card');
        card.appendChild(avatarNode(user, 240));
        card.appendChild(el('div', 'pp-modal-name', user.Name));
        card.appendChild(el('div', 'pp-modal-hint', opts.pin
            ? 'Saisis le code PIN de ce profil.'
            : 'Saisis ton mot de passe pour accéder à ce profil.'));

        var form = el('form', 'pp-pw-form');
        var getValue;
        if (opts.pin) {
            var len = Math.max(4, Math.min(8, parseInt(opts.length, 10) || 4));
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
            getValue = function () { return boxes.map(function (x) { return x.value; }).join(''); };
            modal.__focus = function () { boxes[0].focus(); };
            modal.__reset = function () {
                boxes.forEach(function (x) { x.value = ''; });
                boxes[0].focus();
            };
        } else {
            var hiddenUser = el('input', 'pp-sr-only');
            hiddenUser.type = 'text';
            hiddenUser.name = 'username';
            hiddenUser.autocomplete = 'username';
            hiddenUser.value = user.Name;
            hiddenUser.tabIndex = -1;
            form.appendChild(hiddenUser);
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
            if (modal && modal.classList.contains('pp-busy')) {
                return;
            }
            var v = getValue();
            if (opts.pin && v.length < (parseInt(opts.length, 10) || 4)) {
                return;
            }
            err.textContent = '';
            opts.onSubmit(v);
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

    function showModalError(msg) {
        if (!modal) {
            return;
        }
        modal.__error.textContent = msg;
        modal.classList.remove('pp-shake');
        void modal.offsetWidth;
        modal.classList.add('pp-shake');
        modal.__reset();
    }

    function setBusy(b) {
        [overlay, modal].forEach(function (n) {
            if (n) {
                n.classList.toggle('pp-busy', b);
            }
        });
    }

    // ================================================================== //
    // MODE FOYER
    // ================================================================== //
    function loadHousehold() {
        return apiCall('GET', 'ProfilePicker/Profiles');
    }

    function profileUser(p) {
        return { Id: p.id, Name: p.name, PrimaryImageTag: p.imageTag, locked: p.hasPin };
    }

    function showHouseholdPicker(data, forced, manage) {
        var ui = buildOverlay('household', data.household || serverName);
        overlay.dataset.forced = forced ? '1' : '';
        if (manage) {
            overlay.classList.add('pp-manage');
            overlay.querySelector('.pp-title').textContent = 'Gérer les profils';
        }
        var current = norm(data.currentUserId);
        var reopen = function (m) {
            loadHousehold().then(function (d) {
                hh.data = d;
                showHouseholdPicker(d, forced, m);
            });
        };

        data.profiles.forEach(function (p, i) {
            var user = profileUser(p);
            addTile(ui.grid, user, i, function () {
                if (manage) {
                    editProfile(p, data, function () { reopen(true); });
                    return;
                }
                if (norm(p.id) === current) {
                    sessionStorage.setItem(CHOSEN_KEY, current);
                    removeOverlay();
                    return;
                }
                if (p.hasPin) {
                    openSecretModal(user, {
                        pin: true,
                        length: data.pinLength,
                        onSubmit: function (v) { switchTo(user, v); }
                    });
                } else {
                    switchTo(user, null);
                }
            }, !forced && !manage && norm(p.id) === current);
        });

        if (data.canManage && data.profiles.length < (data.maxProfiles || 6)) {
            var add = el('button', 'pp-profile pp-add');
            add.type = 'button';
            add.style.animationDelay = (data.profiles.length * 45) + 'ms';
            var box = el('div', 'pp-avatar pp-add-box');
            box.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="currentColor" d="M11 5h2v6h6v2h-6v6h-2v-6H5v-2h6z"/></svg>';
            add.appendChild(box);
            add.appendChild(el('span', 'pp-name', 'Ajouter un profil'));
            add.addEventListener('click', function () {
                editProfile(null, data, function () { reopen(manage); });
            });
            ui.grid.appendChild(add);
        }

        if (manage) {
            addAction(ui.actions, 'Terminé', function () { reopen(false); });
        } else {
            if (data.canManage) {
                addAction(ui.actions, 'Gérer les profils', function () { reopen(true); });
            }
            if (!forced) {
                addAction(ui.actions, 'Annuler', function () {
                    sessionStorage.setItem(CHOSEN_KEY, current);
                    removeOverlay();
                });
            }
            addAction(ui.actions, 'Se déconnecter', signOut);
        }
        overlay.classList.remove('pp-loading');
    }

    // Création (p == null) ou modification d'un profil.
    function editProfile(p, data, done) {
        var isCurrent = p && norm(p.id) === norm(data.currentUserId);
        if (p && p.isAdmin && !isCurrent) {
            alert('Un profil administrateur ne se modifie que depuis ce profil.');
            return;
        }
        if (p && p.hasPin && !isCurrent) {
            openSecretModal(profileUser(p), {
                pin: true,
                length: data.pinLength,
                onSubmit: function (v) { closeModal(); openProfileForm(p, data, v, done); }
            });
            return;
        }
        openProfileForm(p, data, null, done);
    }

    function openProfileForm(p, data, currentPin, done) {
        closeModal();
        var creating = !p;
        var chosenAvatar = null;
        var pinLen = data.pinLength || 4;
        modal = el('div', 'pp-modal');
        applyTheme(modal);
        var card = el('div', 'pp-modal-card pp-picker pp-form');
        card.appendChild(el('h2', 'pp-picker-title', creating ? 'Ajouter un profil' : 'Modifier le profil'));

        var head = el('div', 'pp-form-head');
        var preview = el('div', 'pp-form-avatar');
        var setPreview = function (src, user) {
            preview.innerHTML = '';
            if (src) {
                var im = el('img'); im.src = src; im.alt = ''; preview.appendChild(im);
            } else {
                preview.appendChild(avatarNode(user || { Name: '?' }, 200));
            }
        };
        setPreview(null, p ? profileUser(p) : { Name: '+' });
        head.appendChild(preview);
        var fields = el('div', 'pp-form-fields');
        var name = el('input', 'pp-pw');
        name.type = 'text';
        name.maxLength = 32;
        name.placeholder = 'Nom du profil';
        name.value = p ? p.name : '';
        fields.appendChild(name);

        var pin = el('input', 'pp-pw');
        pin.type = 'password';
        pin.inputMode = 'numeric';
        pin.maxLength = pinLen;
        pin.autocomplete = 'new-password';
        pin.placeholder = (p && p.hasPin) ? 'Nouveau PIN (laisser vide = inchangé)' : 'PIN à ' + pinLen + ' chiffres (facultatif)';
        fields.appendChild(pin);

        var removePin = null;
        if (p && p.hasPin && !p.isAdmin) {
            var lbl = el('label', 'pp-check');
            removePin = el('input'); removePin.type = 'checkbox';
            lbl.appendChild(removePin);
            lbl.appendChild(document.createTextNode(' Retirer le PIN'));
            fields.appendChild(lbl);
        }
        head.appendChild(fields);
        card.appendChild(head);

        card.appendChild(el('h3', 'pp-picker-cat', 'Choisis un avatar'));
        var body = el('div', 'pp-picker-body');
        card.appendChild(body);
        getJSON(BASE + '/ProfilePicker/Avatars').then(function (list) {
            var g = el('div', 'pp-picker-grid');
            list.forEach(function (a) {
                var b = el('button', 'pp-picker-item');
                b.type = 'button';
                b.title = a.name;
                var img = el('img'); img.loading = 'lazy'; img.alt = a.name;
                img.src = BASE + '/ProfilePicker/Avatars/' + encodeURIComponent(a.id);
                b.appendChild(img);
                b.addEventListener('click', function () {
                    chosenAvatar = a.id;
                    g.querySelectorAll('.pp-selected').forEach(function (x) { x.classList.remove('pp-selected'); });
                    b.classList.add('pp-selected');
                    setPreview(img.src);
                });
                g.appendChild(b);
            });
            body.appendChild(g);
            if (creating && list.length) {
                // Avatar aléatoire par défaut, comme les plateformes de streaming.
                g.children[Math.floor(Math.random() * list.length)].click();
            }
        });

        var err = el('div', 'pp-error');
        err.setAttribute('role', 'alert');
        card.appendChild(err);

        var actions = el('div', 'pp-modal-actions');
        if (p && !p.isAccount && !p.isAdmin && norm(p.id) !== norm(data.currentUserId)) {
            var del = el('button', 'pp-btn pp-btn-ghost pp-danger', 'Supprimer');
            del.type = 'button';
            del.addEventListener('click', function () {
                if (!confirm('Supprimer le profil « ' + p.name + ' » ? Son historique et ses favoris seront perdus.')) {
                    return;
                }
                modal.classList.add('pp-busy');
                apiCall('POST', 'ProfilePicker/Profiles/' + p.id + '/Delete', { CurrentPin: currentPin }).then(function () {
                    closeModal(); done();
                }, function (resp) { fail(resp); });
            });
            actions.appendChild(del);
        }
        var cancel = el('button', 'pp-btn pp-btn-ghost', 'Annuler');
        cancel.type = 'button';
        cancel.addEventListener('click', closeModal);
        var save = el('button', 'pp-btn pp-btn-primary', creating ? 'Créer' : 'Enregistrer');
        save.type = 'button';
        actions.appendChild(cancel);
        actions.appendChild(save);
        card.appendChild(actions);

        function fail(resp) {
            modal && modal.classList.remove('pp-busy');
            readError(resp).then(function (e) {
                err.textContent = (e.body && (e.body.message || e.body.Message)) || ('Erreur ' + e.status);
            });
        }

        save.addEventListener('click', function () {
            var v = name.value.trim();
            if (!v) { err.textContent = 'Indique un nom.'; name.focus(); return; }
            if (pin.value && !new RegExp('^\\d{' + pinLen + '}$').test(pin.value)) {
                err.textContent = 'Le PIN doit contenir ' + pinLen + ' chiffres.'; pin.focus(); return;
            }
            modal.classList.add('pp-busy');
            var req = creating
                ? apiCall('POST', 'ProfilePicker/Profiles', { Name: v, AvatarId: chosenAvatar, Pin: pin.value || null })
                : apiCall('POST', 'ProfilePicker/Profiles/' + p.id + '/Update', {
                    Name: v !== p.name ? v : null,
                    AvatarId: chosenAvatar,
                    Pin: pin.value || null,
                    RemovePin: removePin ? removePin.checked : null,
                    CurrentPin: currentPin
                });
            req.then(function () { closeModal(); done(); }, fail);
        });

        modal.addEventListener('keydown', function (e) { if (e.key === 'Escape') { closeModal(); } });
        modal.addEventListener('click', function (e) { if (e.target === modal) { closeModal(); } });
        modal.appendChild(card);
        document.body.appendChild(modal);
        setTimeout(function () { name.focus(); }, 30);
    }

    function switchTo(user, pin) {
        setBusy(true);
        apiCall('POST', 'ProfilePicker/Profiles/' + encodeURIComponent(user.Id) + '/Switch', { Pin: pin || null })
            .then(applySession)
            .catch(function (resp) {
                setBusy(false);
                return readError(resp).then(function (e) {
                    var msg;
                    if (e.status === 422) {
                        msg = 'Code PIN incorrect.';
                    } else if (e.status === 429) {
                        msg = 'Trop d\'essais. Réessaie dans ' + Math.ceil(((e.body && (e.body.retryAfter || e.body.RetryAfter)) || 300) / 60) + ' min.';
                    } else if (e.status === 403) {
                        msg = (e.body && (e.body.message || e.body.Message)) || 'Accès refusé.';
                    } else {
                        msg = 'Connexion au serveur impossible.';
                    }
                    if (modal) {
                        showModalError(msg);
                    } else {
                        alert(msg);
                    }
                });
            });
    }

    function applySession(result) {
        var api = window.ApiClient;
        var newId = norm(result && result.User && result.User.Id);
        sessionStorage.setItem(CHOSEN_KEY, newId);
        try {
            if (api && typeof api.onAuthenticated === 'function') {
                api.onAuthenticated(api, result);
            } else {
                writeCredentials(result);
            }
        } catch (e) {
            log('onAuthenticated a échoué, écriture directe des identifiants', e);
            writeCredentials(result);
        }
        if (overlay) {
            overlay.classList.add('pp-leaving');
        }
        // Rechargement complet : vide les caches (requêtes, vues) de l'ancien utilisateur.
        setTimeout(function () {
            location.hash = '#/home';
            location.reload();
        }, 250);
    }

    function writeCredentials(result) {
        var key = 'jellyfin_credentials';
        var creds = {};
        try {
            creds = JSON.parse(localStorage.getItem(key) || '{}');
        } catch (e) {
            creds = {};
        }
        creds.Servers = creds.Servers || [];
        var s = creds.Servers.filter(function (x) { return x.Id === result.ServerId; })[0];
        if (!s) {
            s = { Id: result.ServerId, ManualAddress: BASE, LastConnectionMode: 2 };
            creds.Servers.push(s);
        }
        s.UserId = result.User.Id;
        s.AccessToken = result.AccessToken;
        s.DateLastAccessed = Date.now();
        localStorage.setItem(key, JSON.stringify(creds));
    }

    function signOut() {
        removeOverlay();
        sessionStorage.removeItem(CHOSEN_KEY);
        if (window.Dashboard && typeof window.Dashboard.logout === 'function') {
            window.Dashboard.logout();
        } else if (window.ApiClient) {
            window.ApiClient.logout().then(function () {
                location.hash = '#/login';
                location.reload();
            });
        }
    }

    function openSwitcher() {
        loadHousehold().then(function (data) {
            hh.data = data;
            if (!data || !data.household) {
                alert('Ce compte n\'est rattaché à aucun foyer.');
                return;
            }
            showHouseholdPicker(data, false);
        }, function () {
            alert('Impossible de charger les profils.');
        });
    }

    function checkHousehold() {
        var api = window.ApiClient;
        if (!api || typeof api.getCurrentUserId !== 'function' || !api.accessToken || !api.accessToken()) {
            return;
        }
        var uid = norm(api.getCurrentUserId());
        if (!uid || hh.loading || hh.userId === uid) {
            return;
        }
        hh = { userId: uid, data: null, loading: true };
        loadHousehold().then(function (data) {
            hh.loading = false;
            hh.data = data;
            if (!data || !data.household) {
                return;
            }
            var chosen = sessionStorage.getItem(CHOSEN_KEY) === uid;
            if (data.isGate) {
                showHouseholdPicker(data, true);
            } else if (CFG.alwaysAskProfile && !chosen) {
                showHouseholdPicker(data, false);
            } else {
                sessionStorage.setItem(CHOSEN_KEY, uid);
            }
        }, function (e) {
            hh.loading = false;
            log('ProfilePicker/Profiles indisponible', e && e.status);
        });
    }

    // « Changer de profil » dans le menu utilisateur (nouvelle interface) et la page Paramètres (ancienne).
    function ensureSwitchEntries() {
        if (!hh.data || !hh.data.household) {
            return;
        }
        var list = document.querySelector('#app-user-menu [role="menu"]');
        if (list && !list.querySelector('.pp-switch-item')) {
            var ref = list.querySelector('[role="menuitem"]');
            var li = el('li', (ref ? ref.className : '') + ' pp-switch-item');
            li.setAttribute('role', 'menuitem');
            li.tabIndex = -1;
            var ic = el('span', 'material-icons pp-switch-icon', 'switch_account');
            ic.setAttribute('aria-hidden', 'true');
            li.appendChild(ic);
            li.appendChild(el('span', null, 'Changer de profil'));
            li.addEventListener('click', function () {
                list.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
                var backdrop = document.querySelector('#app-user-menu .MuiBackdrop-root');
                if (backdrop) {
                    backdrop.click();
                }
                openSwitcher();
            });
            list.insertBefore(li, list.firstChild);
        }

        var prefs = activePage('myPreferencesMenuPage');
        if (prefs && !prefs.querySelector('.pp-switch-link')) {
            var anchor = prefs.querySelector('.lnkUserProfile');
            if (anchor) {
                var a = el('a', 'listItem-border pp-switch-link');
                a.href = '#';
                a.style.cssText = 'display:block;margin:0;padding:0;color:inherit;text-decoration:none';
                a.innerHTML = '<div class="listItem"><span class="material-icons listItemIcon listItemIcon-transparent switch_account" aria-hidden="true"></span>'
                    + '<div class="listItemBody"><div class="listItemBodyText">Changer de profil</div></div></div>';
                a.addEventListener('click', function (e) {
                    e.preventDefault();
                    openSwitcher();
                });
                anchor.parentNode.insertBefore(a, anchor.nextSibling);
            }
        }
    }

    // ================================================================== //
    // MODE PUBLIC
    // ================================================================== //
    function showPublicOverlay(page) {
        var ui = buildOverlay('public', serverName);
        getJSON(BASE + '/Users/Public').then(function (users) {
            if (!overlay || overlay.dataset.kind !== 'public') {
                return;
            }
            if (!users || !users.length) {
                dismiss(page);
                return;
            }
            users.forEach(function (u, i) {
                u.locked = u.HasPassword;
                addTile(ui.grid, u, i, function () {
                    if (pending) {
                        return;
                    }
                    if (!u.HasPassword) {
                        doLogin(u, '');
                    } else {
                        openPublicSecret(u);
                    }
                });
            });
            var quick = page.querySelector('.btnQuick');
            if (CFG.showQuickConnect && quick && !quick.classList.contains('hide')) {
                addAction(ui.actions, 'Quick Connect', function () {
                    dismiss(page);
                    quick.click();
                });
            }
            if (CFG.showManualLogin) {
                addAction(ui.actions, 'Autre utilisateur', function () {
                    dismiss(page);
                    var b = page.querySelector('.btnManual');
                    if (b) {
                        b.click();
                    }
                });
            }
            overlay.classList.remove('pp-loading');
        }).catch(function (e) {
            log('Users/Public indisponible', e);
            dismiss(page);
        });
    }

    function openPublicSecret(u) {
        openSecretModal(u, { pin: CFG.pinMode, length: CFG.pinLength, onSubmit: function (v) { doLogin(u, v); } });
    }

    function dismiss(page) {
        dismissedPage = page;
        removeOverlay();
    }

    function doLogin(user, password) {
        var page = activePage('loginPage');
        var form = page && page.querySelector('.manualLoginForm');
        var name = page && page.querySelector('#txtManualName');
        var pw = page && page.querySelector('#txtManualPassword');
        if (!form || !name || !pw) {
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
            openPublicSecret(user);
        }
        showModalError(msg);
    }

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

    // ================================================================== //
    // Avatars prédéfinis
    // ================================================================== //
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

    function applyAvatar(userId, avatarId) {
        return fetch(BASE + '/ProfilePicker/Avatars/' + encodeURIComponent(avatarId)).then(function (r) {
            if (!r.ok) {
                throw new Error('avatar introuvable');
            }
            return r.blob();
        }).then(function (blob) {
            var api = window.ApiClient;
            return api.uploadUserImage(userId, 'Primary', new File([blob], 'avatar.png', { type: blob.type || 'image/png' }));
        });
    }

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
                    return applyAvatar(userId, a.id).then(function () { location.reload(); });
                }
            });
        });
    }

    // ================================================================== //
    // Boucle de détection (SPA)
    // ================================================================== //
    function check() {
        checkTimer = null;
        if (!CFG || !CFG.enabled) {
            return;
        }
        var login = activePage('loginPage');

        if (CFG.mode === 'public') {
            if (login) {
                if (!overlay && dismissedPage !== login && !pending) {
                    showPublicOverlay(login);
                }
            } else {
                if (overlay && overlay.dataset.kind === 'public') {
                    removeOverlay();
                }
                pending = null;
                dismissedPage = null;
            }
        } else if (login) {
            // Page de connexion : formulaire natif (compte foyer). On réinitialise l'état.
            if (overlay && overlay.dataset.kind === 'household') {
                removeOverlay();
            }
            sessionStorage.removeItem(CHOSEN_KEY);
            hh = { userId: null, data: null, loading: false };
        } else {
            checkHousehold();
            ensureSwitchEntries();
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
        switchProfile: openSwitcher
    };

    getJSON(BASE + '/System/Info/Public').then(function (i) {
        serverName = (i && i.ServerName) || '';
        var s = overlay && overlay.querySelector('.pp-server');
        if (s && !s.textContent) {
            s.textContent = serverName;
        }
    }).catch(function () {});

    getJSON(BASE + '/ProfilePicker/Config').then(start, function (e) {
        log('configuration indisponible, valeurs par défaut', e);
        start(null);
    });
})();
