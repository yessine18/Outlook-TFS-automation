document.addEventListener('DOMContentLoaded', () => {
  const slides = document.querySelectorAll('.slide');
  const totalSlides = slides.length;
  let current = 0;

  const progress = document.getElementById('progress');
  const counter = document.getElementById('slide-counter');
  const prevBtn = document.getElementById('prev-btn');
  const nextBtn = document.getElementById('next-btn');

  function showSlide(n) {
    slides[current].classList.remove('active');
    current = Math.max(0, Math.min(n, totalSlides - 1));
    slides[current].classList.add('active');
    progress.style.width = ((current + 1) / totalSlides * 100) + '%';
    counter.textContent = (current + 1) + ' / ' + totalSlides;
    prevBtn.disabled = current === 0;
    nextBtn.disabled = current === totalSlides - 1;

    // Render mermaid diagrams on active slide if not yet rendered
    const mermaidEls = slides[current].querySelectorAll('.mermaid:not([data-processed])');
    if (mermaidEls.length > 0 && window.mermaid) {
      mermaid.run({ nodes: mermaidEls });
    }
  }

  prevBtn.addEventListener('click', () => showSlide(current - 1));
  nextBtn.addEventListener('click', () => showSlide(current + 1));

  document.addEventListener('keydown', (e) => {
    if (e.key === 'ArrowRight' || e.key === ' ' || e.key === 'PageDown') {
      e.preventDefault(); showSlide(current + 1);
    }
    if (e.key === 'ArrowLeft' || e.key === 'PageUp') {
      e.preventDefault(); showSlide(current - 1);
    }
    if (e.key === 'Home') { e.preventDefault(); showSlide(0); }
    if (e.key === 'End') { e.preventDefault(); showSlide(totalSlides - 1); }
    if (e.key === 'f' || e.key === 'F') {
      if (!document.fullscreenElement) document.documentElement.requestFullscreen();
      else document.exitFullscreen();
    }
  });

  // Touch support
  let touchStartX = 0;
  document.addEventListener('touchstart', e => { touchStartX = e.changedTouches[0].screenX; });
  document.addEventListener('touchend', e => {
    const diff = touchStartX - e.changedTouches[0].screenX;
    if (Math.abs(diff) > 60) {
      if (diff > 0) showSlide(current + 1);
      else showSlide(current - 1);
    }
  });

  showSlide(0);
});
