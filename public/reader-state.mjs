export function validateStrategy(data) {
  if (data?.format !== 'raikou-web-v1' || !Array.isArray(data.nodes) || !data.nodes.length ||
      data.first !== 3146 || data.last !== 3359 || data.target !== 3484 || data.seed !== '0x100A0D2A') throw new Error('Invalid strategy data.');
  const seen = new Set(), origins = new Set();
  function visit(id) {
    if (!Number.isInteger(id) || !data.nodes[id] || seen.has(id)) throw new Error('Invalid strategy branch.');
    seen.add(id);
    const node = data.nodes[id];
    if (!Array.isArray(node.steps) || !Array.isArray(node.choices) || !Array.isArray(node.startingAdvances) ||
        !Number.isInteger(node.remaining) || node.remaining < 1 || node.steps.some(step => step.kind === 'entry')) throw new Error('Invalid route.');
    if (node.terminal) {
      if (node.choices.length || node.startingAdvances.length !== node.remaining || node.steps.at(-1)?.kind !== 'finish') throw new Error('Invalid finish.');
      for (const start of node.startingAdvances) {
        if (!Number.isInteger(start) || start < data.first || start > data.last || origins.has(start)) throw new Error('Invalid starting advance.');
        origins.add(start);
      }
    } else {
      if (node.choices.length < 2 || node.startingAdvances.length || node.steps.at(-1)?.kind !== 'observe') throw new Error('Invalid observation.');
      let total = 0;
      for (const choice of node.choices) {
        if (choice.next <= id || !Array.isArray(choice.cues) || typeof choice.search !== 'string') throw new Error('Invalid response.');
        total += visit(choice.next);
        if (choice.count !== data.nodes[choice.next].remaining) throw new Error('Invalid response count.');
      }
      if (total !== node.remaining) throw new Error('Incomplete response coverage.');
    }
    return node.remaining;
  }
  if (visit(0) !== 214 || origins.size !== 214 || seen.size !== data.nodes.length) throw new Error('Incomplete strategy.');
  return data;
}

export class ReaderState {
  constructor(data) { this.data = validateStrategy(data); this.node = 0; this.history = []; this.revision = 0; }
  get view() { return this.data.nodes[this.node]; }
  choose(index, revision = this.revision) {
    const choice = this.view.choices[index];
    if (revision !== this.revision || !choice) return false;
    this.history.push(this.node); this.node = choice.next; this.revision++; return true;
  }
  back() { if (!this.history.length) return false; this.node = this.history.pop(); this.revision++; return true; }
  restart() { this.node = 0; this.history = []; this.revision++; }
}
