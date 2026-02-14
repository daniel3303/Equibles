// Main styles (Tailwind v4 + DaisyUI v5)
import './css/main.css';

// AOS (Animate On Scroll) — scroll-triggered animations (respects reduced motion)
import AOS from 'aos';
import 'aos/dist/aos.css';
if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    AOS.init({ once: true, duration: 400 });
} else {
    // AOS CSS sets [data-aos] elements to opacity:0 — force them visible when AOS is skipped
    document.addEventListener('DOMContentLoaded', () => {
        document.querySelectorAll('[data-aos]').forEach(el => el.classList.add('aos-animate'));
    });
}

// Chart.js (global for inline chart scripts)
import Chart from 'chart.js/auto';
window.Chart = Chart;

// Custom modules
import './js/loader';
import './js/messages';
import './js/modals';
import './js/homepage-demos';
import './js/docs-nav';
