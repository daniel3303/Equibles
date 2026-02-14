// homepage-demos.js — Animated AI agent demos on the homepage
// Hero demo: Typed.js typewriter → data-delay timeline, cycles between .hero-cycle groups forever
// Scroll demos: IntersectionObserver at threshold 0.3 → query fade → timeline
// All respect prefers-reduced-motion by showing final state immediately.

import Typed from 'typed.js';

const SPINNER_SVG = `<svg class="w-4 h-4 animate-spin text-base-content/40" viewBox="0 0 24 24" fill="none">
    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3"></circle>
    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"></path>
</svg>`;

const CHECK_SVG = `<svg class="w-4 h-4 text-success" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
    <polyline points="20 6 9 17 4 12"></polyline>
</svg>`;

const HERO_PAUSE_MS = 5000;
const HERO_FADE_MS = 500;
const STEP_COMPLETE_OFFSET_MS = 1000;

let heroTyped = null;
let heroCycleTimeouts = [];

function reducedMotion() {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

// Hero demo — cycles between .hero-cycle groups forever
function initHeroDemo() {
    const container = document.getElementById('hero-demo');
    if (!container) return;

    if (reducedMotion()) {
        showAllImmediately();
        return;
    }

    runHeroCycle(container, 0);
}

// Run one hero cycle: show cycle group → type search → animate timeline → pause → fade → next
function runHeroCycle(container, cycleIndex) {
    const cycles = container.querySelectorAll('.hero-cycle');
    if (!cycles.length) return;

    const activeCycle = cycles[cycleIndex % cycles.length];
    const searchBar = container.querySelector('.hero-search-bar');

    // Clear pending timeouts from previous cycle
    heroCycleTimeouts.forEach(id => clearTimeout(id));
    heroCycleTimeouts = [];

    // Destroy previous Typed.js instance
    if (heroTyped) {
        heroTyped.destroy();
        heroTyped = null;
    }

    // Hide all cycles, show the active one
    cycles.forEach(c => c.classList.add('hidden'));
    activeCycle.classList.remove('hidden');

    // Reset all animatable elements in the active cycle
    resetCycleElements(activeCycle);

    // Clear typed target
    const typedTarget = container.querySelector('#hero-typed');
    if (typedTarget) typedTarget.textContent = '';

    // Start typing the search text from data-search attribute
    const searchText = activeCycle.dataset.search;
    heroTyped = new Typed('#hero-typed', {
        strings: [searchText],
        typeSpeed: 40,
        showCursor: true,
        cursorChar: '|',
        onComplete: () => {
            const timelineDuration = runCycleTimeline(activeCycle);

            // After timeline + pause, fade out individual children and start next cycle
            const fadeOutTimeout = setTimeout(() => {
                const fadeTargets = [
                    ...activeCycle.querySelectorAll('.demo-step, .demo-row, .demo-complete'),
                ];
                if (searchBar) fadeTargets.push(searchBar);
                fadeTargets.forEach(el => el.classList.add('hero-fade-out'));

                const nextTimeout = setTimeout(() => {
                    fadeTargets.forEach(el => el.classList.remove('hero-fade-out'));

                    const nextIndex = (cycleIndex + 1) % cycles.length;
                    runHeroCycle(container, nextIndex);
                }, HERO_FADE_MS);
                heroCycleTimeouts.push(nextTimeout);
            }, timelineDuration + HERO_PAUSE_MS);
            heroCycleTimeouts.push(fadeOutTimeout);
        }
    });
}

// Generic data-delay driven timeline — reads data-delay from each element
function runCycleTimeline(cycleEl) {
    const elements = cycleEl.querySelectorAll('[data-delay]');
    let maxDelay = 0;

    elements.forEach(el => {
        const delay = parseInt(el.dataset.delay, 10);

        if (el.classList.contains('demo-step')) {
            const revealId = setTimeout(() => revealStep(el), delay);
            const completeId = setTimeout(() => completeStep(el), delay + STEP_COMPLETE_OFFSET_MS);
            heroCycleTimeouts.push(revealId, completeId);
        } else {
            const revealId = setTimeout(() => reveal(el), delay);
            heroCycleTimeouts.push(revealId);
        }

        const effectiveEnd = el.classList.contains('demo-step')
            ? delay + STEP_COMPLETE_OFFSET_MS
            : delay;
        maxDelay = Math.max(maxDelay, effectiveEnd);
    });

    return maxDelay + 300; // buffer for final transition
}

// Reset cycle elements to their initial hidden state
function resetCycleElements(cycleEl) {
    cycleEl.querySelectorAll('.demo-step').forEach(step => {
        step.classList.add('opacity-0', 'translate-y-2');
        step.querySelector('.demo-step-icon').innerHTML = '';
        const textEl = step.querySelector('.demo-step-text');
        textEl.textContent = textEl.dataset.loading;
    });

    cycleEl.querySelectorAll('.demo-row, .demo-complete').forEach(el => {
        el.classList.add('opacity-0', 'translate-y-2');
    });
}

// Scroll demos — IntersectionObserver triggers query fade then timeline
function initScrollDemos() {
    const demos = document.querySelectorAll('.scroll-demo');
    if (!demos.length) return;
    if (reducedMotion()) return; // showAllImmediately already handled

    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (!entry.isIntersecting) return;
            const container = entry.target;
            observer.unobserve(container);

            // Fade in query text (no Typed.js — keeps pace snappy)
            const query = container.querySelector('.demo-query');
            if (query) query.classList.remove('opacity-0');

            // After query settles, run the shared demo timeline
            setTimeout(() => runDemoTimeline(container), 600);
        });
    }, { threshold: 0.3 });

    demos.forEach(demo => observer.observe(demo));
}

// Shared timeline for scroll demos: agent steps → result rows → completion
function runDemoTimeline(container) {
    const steps = container.querySelectorAll('.demo-step');
    const rows = container.querySelectorAll('.demo-row');
    const completion = container.querySelector('.demo-complete');

    // Step 1: searching → connected
    if (steps[0]) {
        setTimeout(() => revealStep(steps[0]), 200);
        setTimeout(() => completeStep(steps[0]), 1200);
    }

    // Step 2: fetching → retrieved
    if (steps[1]) {
        setTimeout(() => revealStep(steps[1]), 1500);
        setTimeout(() => completeStep(steps[1]), 2500);
    }

    // Result rows — staggered 200ms apart
    rows.forEach((row, i) => {
        setTimeout(() => reveal(row), 2800 + i * 200);
    });

    // Completion line
    if (completion) {
        setTimeout(() => reveal(completion), 2800 + rows.length * 200 + 300);
    }
}

// Show element with fade+slide transition
function reveal(el) {
    el.classList.remove('opacity-0', 'translate-y-2');
}

// Show a step with a loading spinner
function revealStep(el) {
    el.querySelector('.demo-step-icon').innerHTML = SPINNER_SVG;
    reveal(el);
}

// Swap spinner for checkmark and update text
function completeStep(el) {
    el.querySelector('.demo-step-icon').innerHTML = CHECK_SVG;
    el.querySelector('.demo-step-text').textContent = el.dataset.completed;
}

// Reduced motion: show final state for all demos without animation
function showAllImmediately() {
    // Hero demo — show first cycle's final state
    const hero = document.getElementById('hero-demo');
    if (hero) {
        const firstCycle = hero.querySelector('.hero-cycle');
        if (firstCycle) {
            firstCycle.classList.remove('hidden');
            const typed = hero.querySelector('#hero-typed');
            if (typed) typed.textContent = firstCycle.dataset.search;
            showDemoFinalState(firstCycle);
        }
    }

    // Scroll demos
    document.querySelectorAll('.scroll-demo').forEach(container => {
        const query = container.querySelector('.demo-query');
        if (query) query.classList.remove('opacity-0');
        showDemoFinalState(container);
    });
}

function showDemoFinalState(container) {
    container.querySelectorAll('.demo-step').forEach(step => {
        step.classList.remove('opacity-0', 'translate-y-2');
        step.querySelector('.demo-step-icon').innerHTML = CHECK_SVG;
        step.querySelector('.demo-step-text').textContent = step.dataset.completed;
    });

    container.querySelectorAll('.demo-row, .demo-complete').forEach(el => {
        el.classList.remove('opacity-0', 'translate-y-2');
    });
}

document.addEventListener('DOMContentLoaded', () => {
    initHeroDemo();
    initScrollDemos();
});
