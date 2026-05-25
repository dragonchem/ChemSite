function centerActivePage() {
    const viewport = document.querySelector('.page-center-viewport');
    const ul = viewport && viewport.querySelector('.pagination');
    const active = ul && ul.querySelector('.page-item.active');
    if (!viewport || !ul || !active) return;

    ul.style.transform = '';
    const activeCenter = active.offsetLeft + active.offsetWidth / 2;
    const translateX = viewport.offsetWidth / 2 - activeCenter;
    ul.style.transform = 'translateX(' + translateX + 'px)';
}

document.addEventListener('DOMContentLoaded', centerActivePage);
window.addEventListener('resize', centerActivePage);
