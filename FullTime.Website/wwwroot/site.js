const form = document.getElementById('contact-form');
const status = document.getElementById('contact-status');
const submitBtn = document.getElementById('contact-submit');

form.addEventListener('submit', async (e) => {
    e.preventDefault();
    submitBtn.disabled = true;
    status.textContent = 'Sending…';
    status.className = 'status';

    const payload = {
        name: form.name.value,
        email: form.email.value,
        message: form.message.value,
    };

    try {
        const res = await fetch('/api/contact', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });
        const data = await res.json().catch(() => ({}));

        if (res.ok) {
            status.textContent = data.message || "Thanks — we'll get back to you soon.";
            status.className = 'status success';
            form.reset();
        } else {
            status.textContent = data.error || data.title || 'Something went wrong — please try again.';
            status.className = 'status error';
        }
    } catch {
        status.textContent = 'Could not reach the server — please try again.';
        status.className = 'status error';
    } finally {
        submitBtn.disabled = false;
    }
});
