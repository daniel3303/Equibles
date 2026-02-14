// Docs sidebar — active section tracking, smooth scroll, mobile toggle

(() => {
    const nav = document.getElementById('docs-nav');
    if (!nav) return;

    const sections = document.querySelectorAll('main section[id]');
    const navLinks = nav.querySelectorAll('a[href^="#"]');
    const toggle = document.getElementById('docs-nav-toggle');

    // Track active section via IntersectionObserver
    const observer = new IntersectionObserver((entries) => {
        const visible = entries
            .filter(e => e.isIntersecting)
            .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
        if (visible.length > 0) {
            navLinks.forEach(l => l.classList.remove('active'));
            nav.querySelector(`a[href="#${visible[0].target.id}"]`)?.classList.add('active');
        }
    }, { rootMargin: '-80px 0px -65% 0px' });

    // Set initial active link (before observer starts, so it isn't overridden)
    const hash = window.location.hash.slice(1);
    const initial = hash ? nav.querySelector(`a[href="#${hash}"]`) : navLinks[0];
    initial?.classList.add('active');

    // Delay observer start so initial active state isn't immediately overridden
    setTimeout(() => {
        sections.forEach(s => observer.observe(s));
    }, 500);

    // Smooth scroll on nav link click
    navLinks.forEach(link => {
        link.addEventListener('click', (e) => {
            e.preventDefault();
            const id = link.getAttribute('href').slice(1);
            document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' });
            history.pushState(null, '', `#${id}`);
            if (window.innerWidth < 1024) closeMobileNav();
        });
    });

    // Mobile toggle
    function closeMobileNav() {
        nav.classList.add('hidden');
        nav.classList.remove('block');
        toggle?.setAttribute('aria-expanded', 'false');
    }

    if (toggle) {
        toggle.addEventListener('click', () => {
            const isOpening = nav.classList.contains('hidden');
            nav.classList.toggle('hidden', !isOpening);
            nav.classList.toggle('block', isOpening);
            toggle.setAttribute('aria-expanded', String(isOpening));
        });
    }

    // Close mobile nav on outside click or Escape
    document.addEventListener('click', (e) => {
        if (window.innerWidth < 1024
            && !nav.classList.contains('hidden')
            && !nav.contains(e.target)
            && e.target !== toggle
            && !toggle?.contains(e.target)) {
            closeMobileNav();
        }
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && !nav.classList.contains('hidden') && window.innerWidth < 1024) {
            closeMobileNav();
        }
    });
})();
