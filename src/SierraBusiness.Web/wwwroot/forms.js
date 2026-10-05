// Keep form pickers inside the page. WebView2's separate native popup windows
// are unreliable in some Windows hosts; the original inputs remain bound to Blazor.
(() => {
    let active;
    const disabled = input => input.matches(':disabled') || input.readOnly === true;
    const label = input => input.getAttribute('aria-label') ||
        (input.labels?.[0]?.childNodes ? Array.from(input.labels[0].childNodes)
            .filter(node => node.nodeType === Node.TEXT_NODE).map(node => node.textContent).join(' ').trim() : '') || 'Choose a value';
    const commit = (input, value) => {
        if (!input.isConnected || disabled(input)) { close(); return; }
        input.value = value;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
        close();
    };
    function close() {
        if (!active) return;
        const { input, layer } = active;
        active = undefined;
        layer.remove();
        input.removeAttribute('aria-expanded');
        if (input.isConnected) input.focus({ preventScroll: true });
    }
    function button(text, action, className = '') {
        const result = document.createElement('button');
        result.type = 'button'; result.textContent = text; result.className = className;
        result.addEventListener('click', action);
        return result;
    }
    function open(input) {
        close();
        const layer = document.createElement('div'); layer.className = 'form-picker-layer';
        const panel = document.createElement('section'); panel.className = 'form-picker';
        panel.setAttribute('role', 'dialog'); panel.setAttribute('aria-modal', 'true');
        panel.setAttribute('aria-label', label(input));
        const title = document.createElement('h2'); title.textContent = label(input);
        const header = document.createElement('div'); header.className = 'form-picker-header';
        header.append(title, button('Close', close)); panel.append(header); layer.append(panel);
        layer.addEventListener('click', event => { if (event.target === layer) close(); });
        layer.addEventListener('keydown', event => {
            if (event.key === 'Escape') { event.preventDefault(); close(); }
            if (event.key !== 'Tab') return;
            const focusable = Array.from(panel.querySelectorAll('button:not(:disabled),input:not(:disabled)'));
            const first = focusable[0], last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
            if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
        });
        document.body.append(layer); input.setAttribute('aria-expanded', 'true');
        active = { input, layer }; return panel;
    }
    function select(input) {
        const panel = open(input);
        const search = document.createElement('input'); search.type = 'search';
        search.placeholder = 'Search choices'; search.setAttribute('aria-label', 'Search choices');
        const list = document.createElement('div'); list.className = 'form-picker-options';
        const draw = () => {
            list.replaceChildren();
            for (const option of input.options) {
                if (option.hidden || !option.text.toLocaleLowerCase().includes(search.value.toLocaleLowerCase())) continue;
                const choice = button(option.text, () => commit(input, option.value), option.selected ? 'selected' : '');
                choice.disabled = option.disabled || option.parentElement?.disabled === true;
                choice.setAttribute('aria-pressed', String(option.selected)); list.append(choice);
            }
            if (!list.childElementCount) {
                const empty = document.createElement('p'); empty.textContent = 'No matching choices.'; list.append(empty);
            }
        };
        search.addEventListener('input', draw);
        search.addEventListener('keydown', event => {
            if (event.key === 'ArrowDown') { event.preventDefault(); list.querySelector('button:not(:disabled)')?.focus(); }
            if (event.key === 'Enter') { event.preventDefault(); list.querySelector('button:not(:disabled)')?.click(); }
        });
        list.addEventListener('keydown', event => {
            if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
            event.preventDefault();
            const choices = Array.from(list.querySelectorAll('button:not(:disabled)'));
            const index = choices.indexOf(document.activeElement);
            const next = event.key === 'Home' ? 0 : event.key === 'End' ? choices.length - 1 :
                Math.max(0, Math.min(choices.length - 1, index + (event.key === 'ArrowDown' ? 1 : -1)));
            choices[next]?.focus();
        });
        panel.append(search, list); draw(); search.focus();
    }
    const iso = date => `${String(date.getFullYear()).padStart(4, '0')}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
    const parse = value => {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
        const [year, month, day] = value.split('-').map(Number);
        const result = new Date(2000, month - 1, day); result.setFullYear(year);
        return iso(result) === value ? result : null;
    };
    function calendar(input) {
        const panel = open(input);
        const selected = parse(input.value), today = new Date();
        let month = new Date(selected || today); month.setDate(1);
        const toolbar = document.createElement('div'); toolbar.className = 'form-calendar-toolbar';
        const heading = document.createElement('strong'); heading.setAttribute('aria-live', 'polite');
        const year = document.createElement('input'); year.type = 'number'; year.min = '1'; year.max = '9999';
        year.setAttribute('aria-label', 'Calendar year');
        const days = document.createElement('div'); days.className = 'form-calendar-days';
        const allowed = value => (!input.min || value >= input.min) && (!input.max || value <= input.max);
        const draw = () => {
            heading.textContent = month.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
            year.value = String(month.getFullYear()); days.replaceChildren();
            previous.disabled = month.getFullYear() === 1 && month.getMonth() === 0;
            next.disabled = month.getFullYear() === 9999 && month.getMonth() === 11;
            for (const name of ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']) {
                const weekday = document.createElement('span'); weekday.textContent = name; days.append(weekday);
            }
            const offset = (month.getDay() + 6) % 7;
            for (let i = 0; i < offset; i++) days.append(document.createElement('span'));
            const last = new Date(month); last.setMonth(last.getMonth() + 1, 0);
            const count = last.getDate();
            for (let day = 1; day <= count; day++) {
                const date = new Date(month); date.setDate(day); const value = iso(date);
                const choice = button(String(day), () => commit(input, value), input.value === value ? 'selected' : '');
                choice.setAttribute('aria-label', date.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' }));
                choice.setAttribute('aria-pressed', String(input.value === value));
                choice.disabled = !allowed(value); days.append(choice);
            }
        };
        const previous = button('‹', () => { month.setMonth(month.getMonth() - 1); draw(); });
        previous.setAttribute('aria-label', 'Previous month');
        const next = button('›', () => { month.setMonth(month.getMonth() + 1); draw(); });
        next.setAttribute('aria-label', 'Next month');
        year.addEventListener('change', () => {
            const value = Number(year.value);
            if (Number.isInteger(value) && value >= 1 && value <= 9999) month.setFullYear(value);
            draw();
        });
        toolbar.append(previous, heading, next);
        const footer = document.createElement('div'); footer.className = 'form-picker-footer';
        const todayButton = button('Today', () => commit(input, iso(today))); todayButton.disabled = !allowed(iso(today));
        footer.append(todayButton);
        if (!input.required) footer.append(button('Clear date', () => commit(input, '')));
        panel.append(toolbar, year, days, footer); draw(); next.focus();
    }
    const pickerInput = target => target instanceof Element ? target.closest('select:not([multiple]),input[type="date"]') : null;
    // Capture prevents Chromium from opening the broken external popup. Change
    // events still bubble normally to the Blazor @bind handlers.
    document.addEventListener('mousedown', event => {
        const input = pickerInput(event.target);
        if (input && !disabled(input) && !(input instanceof HTMLSelectElement && input.size > 1)) event.preventDefault();
    }, true);
    document.addEventListener('click', event => {
        const input = pickerInput(event.target);
        if (!input || disabled(input) || (input instanceof HTMLSelectElement && input.size > 1)) return;
        event.preventDefault();
        input instanceof HTMLSelectElement ? select(input) : calendar(input);
    }, true);
    document.addEventListener('keydown', event => {
        const input = pickerInput(event.target);
        if (!input || disabled(input) || (input instanceof HTMLSelectElement && input.size > 1)) return;
        if ((input instanceof HTMLSelectElement && ['Enter', ' ', 'ArrowDown', 'ArrowUp'].includes(event.key)) ||
            (input instanceof HTMLInputElement && event.altKey && event.key === 'ArrowDown')) {
            event.preventDefault(); input instanceof HTMLSelectElement ? select(input) : calendar(input);
        }
    }, true);
    // A navigation/render may remove the original field while its picker is open.
    new MutationObserver(() => { if (active && (!active.input.isConnected || disabled(active.input))) close(); })
        .observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled'] });
})();
