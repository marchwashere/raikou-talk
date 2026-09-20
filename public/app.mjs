import { ReaderState } from './reader-state.mjs';

const $ = id => document.getElementById(id);
const bandButtons = [...document.querySelectorAll('[data-band]')];
const arrows = ['↑', '↓', '←', '→'];
let manifest, reader, currentBand, generation = 0, controller, retryAction;
const cache = new Map();

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}
function status(text = '', error = false, retry = null) {
  $('status').textContent = text;
  $('status').parentElement.classList.toggle('error', error);
  retryAction = retry; $('retry').hidden = !retry;
  $('bands').setAttribute('aria-busy', String(!!text && !error));
}
async function json(url, signal) {
  const response = await fetch(new URL(url, document.baseURI), { signal, cache: 'no-cache' });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.json();
}
async function initialize() {
  const ticket = ++generation; controller?.abort(); controller = new AbortController();
  bandButtons.forEach(button => button.disabled = true); status('Loading…');
  try {
    const loaded = await json('manifest.json', controller.signal);
    if (ticket !== generation) return;
    if (loaded.format !== 'raikou-web-v1' || loaded.bands?.map(b => b.id).join(',') !== 'Q2,Q3,Q4,Full' || !loaded.emotes) throw new Error('Invalid manifest');
    manifest = loaded; bandButtons.forEach(button => button.disabled = false); status(); bandButtons[0].focus();
  } catch (error) { if (ticket === generation && error.name !== 'AbortError') status('Could not load strategies.', true, initialize); }
}
async function selectBand(id) {
  if (!manifest) return;
  const ticket = ++generation; controller?.abort(); controller = new AbortController(); status('Loading…');
  try {
    const band = manifest.bands.find(b => b.id === id);
    const data = cache.get(id) ?? await json(band.url, controller.signal);
    if (ticket !== generation) return;
    if (data.band !== id) throw new Error('Wrong band');
    const next = new ReaderState(data);
    cache.set(id, data); reader = next; currentBand = band; status();
    $('details').hidden = true; $('details-toggle').setAttribute('aria-expanded', 'false');
    $('bands').hidden = true; $('route').hidden = false; render();
  } catch (error) { if (ticket === generation && error.name !== 'AbortError') status(`Could not load ${id}.`, true, () => selectBand(id)); }
}
function showBands() {
  generation++; controller?.abort(); status(); $('route').hidden = true; $('bands').hidden = false;
  bandButtons.find(button => button.dataset.band === currentBand?.id)?.focus();
}
function cueFragment(cues) {
  const fragment = document.createDocumentFragment();
  for (const cue of cues ?? []) {
    const sprite = manifest.emotes[String(cue.emote)];
    if (sprite) {
      const wrapper = el('span', 'emote'); wrapper.dataset.emote = String(cue.emote);
      wrapper.setAttribute('role', 'img'); wrapper.setAttribute('aria-label', sprite.label);
      const image = el('img'); image.src = sprite.src; image.alt = ''; image.draggable = false;
      wrapper.append(image); fragment.append(wrapper);
    }
    if (cue.text) fragment.append(document.createTextNode(`${cue.text} `));
  }
  return fragment;
}
function renderStep(step, index, view) {
  const row = el('div', 'step'); row.dataset.kind = step.kind;
  const finish = step.kind === 'finish';
  const label = step.kind === 'turn' ? `${step.count} TF` : step.kind === 'approach' ? 'Raikou' : finish ? 'Finish' : 'Talk';
  row.append(el('span', 'ordinal', finish ? '' : String(index + 1)), el('span', 'step-label', label));
  const body = el('div', 'step-content');
  if (step.kind === 'turn') {
    body.classList.add('directions');
    body.textContent = step.runs.map(run => {
      const sequence = run.directions.map(n => arrows[n]).join(' ');
      return run.repeats > 1 ? `(${sequence})×${run.repeats}` : sequence;
    }).join('  ·  ');
  } else if (step.kind === 'approach') { body.classList.add('directions'); body.textContent = '←'; }
  else if (finish) {
    body.textContent = `${reader.data.target} · ${step.seed}`;
    const tail = step.approach === 'bonk-left' ? '   BONK ↑; ← Raikou' : view.steps.some(s => s.kind === 'approach') ? '' : '   ← Raikou';
    body.append(el('span', 'finish-tail', tail));
  } else {
    body.classList.add('cue');
    if (step.cues?.length) body.append(cueFragment(step.cues));
    else if (step.anyResponse) body.textContent = 'Any response';
    if (step.answer && !step.cues?.some(cue => cue.text === `Answer ${step.answer}`)) body.append(document.createTextNode(`${body.textContent ? ' · ' : ''}Answer ${step.answer}`));
  }
  row.append(body); return row;
}
function render() {
  const view = reader.view, revision = reader.revision;
  $('route-title').textContent = `${currentBand.id} · ${currentBand.range}`;
  $('summary').textContent = `${reader.data.seed} · ${view.remaining} start${view.remaining === 1 ? '' : 's'} · ${reader.history.length} observation${reader.history.length === 1 ? '' : 's'}`;
  $('back').disabled = !reader.history.length;
  $('route').classList.toggle('identifying', !view.terminal);
  $('details').replaceChildren(...reader.data.details.map(line => el('p', '', line)));
  $('steps').replaceChildren(...view.steps.map((step, i) => renderStep(step, i, view)));
  $('origins').hidden = !view.terminal;
  $('origins').replaceChildren(document.createTextNode(`B1F starting advance${view.startingAdvances.length === 1 ? '' : 's'}: `), el('span', '', view.startingAdvances.join(', ')));
  $('responses').hidden = view.terminal;
  $('filter').value = ''; $('clear-filter').hidden = true; $('no-match').hidden = true;
  $('choices').replaceChildren(...view.choices.map((choice, index) => {
    const button = el('button', 'choice'); button.type = 'button'; button.dataset.index = String(index);
    const cue = el('span', 'cue'); cue.append(cueFragment(choice.cues));
    button.append(el('span', 'choice-number', index < 9 ? String(index + 1) : '·'), cue);
    button.addEventListener('click', event => {
      if (event.detail > 1) return;
      if (reader.choose(index, revision)) render();
    });
    return button;
  }));
  $('actions').scrollTop = 0; window.scrollTo(0, 0); $('route-title').focus({ preventScroll: true });
}
function filter() {
  const query = $('filter').value.trim().toLocaleLowerCase(); let shown = 0;
  [...$('choices').children].forEach((button, index) => {
    button.hidden = !reader.view.choices[index].search.toLocaleLowerCase().includes(query);
    if (!button.hidden) shown++;
  });
  $('clear-filter').hidden = !query; $('no-match').hidden = shown !== 0;
}
bandButtons.forEach(button => button.addEventListener('click', () => selectBand(button.dataset.band)));
$('retry').addEventListener('click', () => retryAction?.());
$('band').addEventListener('click', showBands);
$('back').addEventListener('click', () => { if (reader.back()) render(); });
$('restart').addEventListener('click', () => { reader.restart(); render(); });
$('details-toggle').addEventListener('click', () => { $('details').hidden = !$('details').hidden; $('details-toggle').setAttribute('aria-expanded', String(!$('details').hidden)); });
$('filter').addEventListener('input', filter);
$('clear-filter').addEventListener('click', () => { $('filter').value = ''; filter(); $('filter').focus(); });
document.addEventListener('keydown', event => {
  if ($('route').hidden || event.repeat) return;
  if (event.altKey && event.key === 'ArrowLeft') { event.preventDefault(); if (reader.back()) render(); return; }
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f' && !reader.view.terminal) { event.preventDefault(); $('filter').focus(); $('filter').select(); return; }
  if (event.ctrlKey || event.metaKey || event.altKey || event.target instanceof HTMLInputElement) return;
  if (/^[1-9]$/.test(event.key)) {
    const button = $('choices').children[Number(event.key) - 1];
    if (button && !button.hidden) { event.preventDefault(); button.click(); }
  }
});
initialize();
