// Synthesized via Web Audio API rather than a shipped audio file, so there's nothing to bundle
// and no licensing to track - a handful of scheduled oscillator "clicks", spaced out with an
// ease-out curve so they sound like a wheel-of-fortune ratchet slowing to a stop.
let sharedCtx = null;

export function playSpinTicks(durationMs) {
    const AudioCtx = window.AudioContext || window.webkitAudioContext;
    if (!AudioCtx) {
        return;
    }

    sharedCtx ??= new AudioCtx();
    const ctx = sharedCtx;
    if (ctx.state === "suspended") {
        ctx.resume();
    }

    const start = ctx.currentTime;
    const totalSeconds = durationMs / 1000;
    const tickCount = 28;

    for (let i = 0; i < tickCount; i++) {
        const progress = i / (tickCount - 1);
        const eased = 1 - Math.pow(1 - progress, 2);
        scheduleClick(ctx, start + eased * totalSeconds);
    }
}

function scheduleClick(ctx, time) {
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = "square";
    osc.frequency.value = 1200;
    gain.gain.setValueAtTime(0.0001, time);
    gain.gain.exponentialRampToValueAtTime(0.25, time + 0.002);
    gain.gain.exponentialRampToValueAtTime(0.0001, time + 0.03);
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.start(time);
    osc.stop(time + 0.04);
}
