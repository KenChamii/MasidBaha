import { labelTableCells, watchTables } from './stack-tables';

const tableHtml = `
  <table class="table">
    <thead><tr><th>Place</th><th><button>Severity ▲</button></th><th></th></tr></thead>
    <tbody>
      <tr><td>Iloilo</td><td>Knee-deep</td><td><button>Open</button></td></tr>
      <tr><td colspan="3">Nothing here</td></tr>
    </tbody>
  </table>`;

describe('stack-tables', () => {
  let host: HTMLElement;

  beforeEach(() => {
    host = document.createElement('div');
    host.innerHTML = tableHtml;
    document.body.appendChild(host);
  });

  afterEach(() => host.remove());

  it('gives each cell the name of its column, without the sort arrow', () => {
    labelTableCells(host.querySelector('table')!);

    const cells = host.querySelectorAll('tbody tr:first-child td');
    expect(cells[0].getAttribute('data-label')).toBe('Place');
    expect(cells[1].getAttribute('data-label')).toBe('Severity');
  });

  it('leaves a column with no heading (the buttons) unlabelled', () => {
    labelTableCells(host.querySelector('table')!);

    expect(host.querySelectorAll('tbody tr:first-child td')[2].getAttribute('data-label')).toBe('');
  });

  it('does not label a message that spans the whole row', () => {
    labelTableCells(host.querySelector('table')!);

    expect(host.querySelector('td[colspan]')!.getAttribute('data-label')).toBe('');
  });

  it('labels rows that are added later', async () => {
    const observer = watchTables(host);
    host.querySelector('tbody')!.insertAdjacentHTML('beforeend', '<tr><td>Cebu</td><td>Ankle</td><td></td></tr>');

    await new Promise(resolve => requestAnimationFrame(() => resolve(null)));
    observer.disconnect();

    expect(host.querySelector('tbody tr:last-child td')!.getAttribute('data-label')).toBe('Place');
  });
});
