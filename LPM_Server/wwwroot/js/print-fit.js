window.printFitOnePage = (zoneId) => {
    const zone = document.getElementById(zoneId);
    if (!zone) { window.print(); return; }

    const scrollDiv = zone.querySelector('.table-responsive');
    const table = zone.querySelector('table');
    if (!table) { window.print(); return; }

    // Make sure we measure the table at its full size
    const origOverflow = scrollDiv ? scrollDiv.style.overflow : '';
    if (scrollDiv) scrollDiv.style.overflow = 'visible';

    // Clear any previous scaling
    zone.style.transform = '';
    zone.style.transformOrigin = '';
    zone.style.width = 'max-content';

    // Force reflow
    table.getBoundingClientRect();

    const rect = table.getBoundingClientRect();
    const tableW = rect.width;
    const tableH = rect.height;

    // A4 landscape content area with 8mm margins
    const pxPerMm = 96 / 25.4;
    const printW = (297 - 16) * pxPerMm; // width minus margins
    const printH = (210 - 16) * pxPerMm; // height minus margins

    const scale = Math.min(printW / tableW, printH / tableH, 1);

    // Use transform scale (more reliable than zoom across browsers)
    zone.style.transformOrigin = 'top left';
    zone.style.transform = `scale(${scale})`;

    // Print only the zone (CSS uses body.printing)
    document.body.classList.add('printing');

    // Let styles apply, then print
    setTimeout(() => {
        window.print();

        // Cleanup after print dialog opens (and after print)
        setTimeout(() => {
            document.body.classList.remove('printing');
            zone.style.transform = '';
            zone.style.transformOrigin = '';
            zone.style.width = '';
            if (scrollDiv) scrollDiv.style.overflow = origOverflow;
        }, 250);
    }, 50);
};