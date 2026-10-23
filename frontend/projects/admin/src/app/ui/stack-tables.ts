// On a phone each table row becomes a card, and every cell needs its column's name beside it.
// The stylesheet reads data-label; this keeps it filled in for every table, whatever page draws it.

const SORT_MARKS = /[▲▼↑↓]/g;

export function labelTableCells(table: HTMLTableElement): void {
  const headings = Array.from(table.tHead?.rows[0]?.cells ?? []).map(cell =>
    (cell.textContent ?? '').replace(SORT_MARKS, '').trim()
  );

  for (const body of Array.from(table.tBodies)) {
    for (const row of Array.from(body.rows)) {
      Array.from(row.cells).forEach((cell, index) => {
        // a message that spans the whole row ("Nothing to review") has no single column
        const label = cell.colSpan > 1 ? '' : (headings[index] ?? '');
        if (cell.getAttribute('data-label') !== label) cell.setAttribute('data-label', label);
      });
    }
  }
}

export function watchTables(root: HTMLElement = document.body): MutationObserver {
  let scheduled = false;

  const apply = (): void => {
    scheduled = false;
    root.querySelectorAll<HTMLTableElement>('table.table').forEach(labelTableCells);
  };

  apply();

  // Rows come and go as pages load; relabel once per frame. Attribute changes are not observed, so this cannot loop.
  const observer = new MutationObserver(() => {
    if (scheduled) return;
    scheduled = true;
    requestAnimationFrame(apply);
  });
  observer.observe(root, { childList: true, subtree: true, characterData: true });
  return observer;
}
