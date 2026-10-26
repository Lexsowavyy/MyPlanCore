(function () {
    'use strict';

    var MIN_QUERY = 2;
    var DEBOUNCE_MS = 250;
    var activeBox = null; // currently-visible dropdown for the focused input

    function isLocationInput(el) {
        if (!el || el.tagName !== 'INPUT') return false;
        var type = (el.type || 'text').toLowerCase();
        if (type !== 'text' && type !== 'search') return false;
        if (el.readOnly || el.disabled) return false;
        if (el.getAttribute('data-location-autocomplete') === 'false') return false;
        if (el.hasAttribute('data-location-autocomplete')) return true;
        var id = (el.id || '').toLowerCase();
        var name = (el.name || '').toLowerCase();
        return id.indexOf('location') !== -1 || name.indexOf('location') !== -1;
    }

    function ensureBox(input) {
        var box = input.__locBox;
        if (box) return box;
        box = document.createElement('div');
        box.className = 'location-suggest-box';
        box.style.cssText = 'display:none;position:fixed;z-index:99999;background:#fff;' +
            'border:1px solid #cbd5e1;border-radius:10px;box-shadow:0 8px 24px rgba(15,23,42,0.18);' +
            'max-height:240px;overflow-y:auto;min-width:220px;';
        document.body.appendChild(box);
        input.__locBox = box;
        return box;
    }

    function positionBox(input) {
        var box = ensureBox(input);
        var rect = input.getBoundingClientRect();
        var width = Math.max(rect.width, 240);
        var left = rect.left;
        if (left + width > window.innerWidth - 8) left = Math.max(8, window.innerWidth - width - 8);
        box.style.width = width + 'px';
        box.style.left = left + 'px';
        box.style.top = (rect.bottom + 4) + 'px';
    }

    function closeBox(input) {
        var box = input.__locBox;
        if (box) { box.style.display = 'none'; box.innerHTML = ''; }
        if (activeBox === box) activeBox = null;
    }

    function selectItem(input, item) {
        input.value = item.label || item.fullName || '';
        closeBox(input);
        input.focus();
        input.dispatchEvent(new Event('input', { bubbles: true }));
    }

    function renderItems(input, items) {
        var box = ensureBox(input);
        box.innerHTML = '';
        if (!items || items.length === 0) {
            if (activeBox === box) { box.style.display = 'none'; }
            return;
        }
        var highlighted = -1;

        function highlight(i) {
            var nodes = box.querySelectorAll('.location-suggest-item');
            for (var k = 0; k < nodes.length; k++) {
                if (k === i) {
                    nodes[k].style.background = '#eff6ff';
                    nodes[k].style.borderLeft = '3px solid #2563eb';
                } else {
                    nodes[k].style.background = '#fff';
                    nodes[k].style.borderLeft = '3px solid transparent';
                }
            }
            var active = nodes[i];
            if (active && active.scrollIntoView) active.scrollIntoView({ block: 'nearest' });
            highlighted = i;
        }

        items.forEach(function (it, i) {
            var node = document.createElement('div');
            node.className = 'location-suggest-item';
            node.style.cssText = 'padding:9px 12px;cursor:pointer;font-size:13px;color:#0f172a;border-bottom:1px solid #f1f5f9;border-left:3px solid transparent;';
            var main = document.createElement('div');
            main.textContent = it.label || it.fullName || '';
            main.style.fontWeight = '500';
            node.appendChild(main);
            if (it.fullName && it.fullName !== it.label) {
                var sub = document.createElement('div');
                sub.textContent = it.fullName;
                sub.style.cssText = 'font-size:11px;color:#64748b;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';
                node.appendChild(sub);
            }
            node.addEventListener('mousedown', function (e) { e.preventDefault(); });
            node.addEventListener('click', function () { selectItem(input, it); });
            node.addEventListener('mouseenter', function () { highlight(i); });
            box.appendChild(node);
        });

        highlight(0);
        positionBox(input);
        box.__input = input;
        box.style.display = 'block';
        activeBox = box;
    }

    function wire(input) {
        if (input.__locWired) return;
        input.__locWired = true;

        var timer = null;
        var lastQuery = '';
        var seq = 0;

        function fetchSuggestions(raw) {
            var q = (raw || '').trim();
            if (q.length < MIN_QUERY) {
                closeBox(input);
                return;
            }
            var key = q.toLowerCase();
            if (key === lastQuery) return;
            lastQuery = key;
            seq++;
            var mySeq = seq;
            clearTimeout(timer);
            timer = setTimeout(function () {
                var url = '/api/locations?q=' + encodeURIComponent(q) + '&limit=8';
                fetch(url, { headers: { 'Accept': 'application/json' } })
                    .then(function (r) { return r.ok ? r.json() : []; })
                    .then(function (data) { if (mySeq === seq) renderItems(input, data); })
                    .catch(function () { if (mySeq === seq) closeBox(input); });
            }, DEBOUNCE_MS);
        }

        input.addEventListener('focus', function () { fetchSuggestions(input.value); });
        input.addEventListener('input', function () { fetchSuggestions(input.value); });

        input.addEventListener('keydown', function (e) {
            var box = input.__locBox;
            if (!box || box.style.display === 'none') return;
            var nodes = box.querySelectorAll('.location-suggest-item');
            var active = -1;
            for (var i = 0; i < nodes.length; i++) {
                if (nodes[i].style.background !== 'rgb(239, 246, 255)' && nodes[i].getAttribute('data-active') === '1') { active = i; break; }
                if (nodes[i].style.borderLeft === '3px solid rgb(37, 99, 235)') { active = i; break; }
            }
            if (active === -1 && nodes.length) active = 0;

            if (e.key === 'ArrowDown') {
                e.preventDefault();
                if (nodes.length) {
                    var next = (active + 1) % nodes.length;
                    nodes[next].dispatchEvent(new MouseEvent('mouseenter', { bubbles: false }));
                }
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                if (nodes.length) {
                    var prev = (active - 1 + nodes.length) % nodes.length;
                    nodes[prev].dispatchEvent(new MouseEvent('mouseenter', { bubbles: false }));
                }
            } else if (e.key === 'Enter' || e.key === 'Tab') {
                if (nodes[active]) {
                    e.preventDefault();
                    nodes[active].click();
                }
            } else if (e.key === 'Escape') {
                closeBox(input);
            }
        });

        input.addEventListener('blur', function () {
            setTimeout(function () {
                if (document.activeElement !== input && document.activeElement !== input.__locBox) closeBox(input);
            }, 150);
        });
    }

    function scanAndWire(root) {
        var inputs = root.querySelectorAll('input[type="text"], input[type="search"]');
        for (var i = 0; i < inputs.length; i++) {
            if (isLocationInput(inputs[i])) wire(inputs[i]);
        }
        // also handle inputs rendered into the DOM after this scan (e.g. modals)
        document.addEventListener('focusin', function (e) {
            var t = e.target;
            if (t && t.tagName === 'INPUT' && isLocationInput(t)) wire(t);
        }, true);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { scanAndWire(document); });
    } else {
        scanAndWire(document);
    }

    window.addEventListener('resize', function () {
        if (activeBox && activeBox.__input) positionBox(activeBox.__input);
    });
})();