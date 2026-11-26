'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const view = fs.readFileSync(path.join(__dirname, '..', 'Views', 'Tenants', 'Config.cshtml'), 'utf8');
function section(start, end) {
    const from = view.indexOf(start), to = view.indexOf(end, from);
    assert.ok(from >= 0 && to > from, `Missing ${start}`);
    return view.slice(from, to);
}
function element() {
    return {
        textContent: '', className: '', title: '', children: [],
        classList: { add() {}, remove() {} },
        append(...children) { this.children.push(...children); },
        replaceChildren() { this.children = []; this.textContent = ''; },
        addEventListener(name, callback) { this[name] = callback; }
    };
}
const elements = new Map();
const byId = id => { if (!elements.has(id)) elements.set(id, element()); return elements.get(id); };
const runs = {};
let shown = false;
const context = {
    document: { getElementById: byId, createElement: element },
    bootstrap: { Modal: { getOrCreateInstance: () => ({ show() { shown = true; } }) } },
    getAutomationRun: key => runs[key] || null,
    openAutomationModal(...args) { context.openArgs = args; },
    _labVmHistory: [], _labVmRun: null, _labVmRemoveMode: false, Date
};
vm.createContext(context);
vm.runInContext(section('        function getAutomationPresentation(', '        function renderDeviceApplySummary('), context);
vm.runInContext(section('        function getLabVmAction(', '        function openDeviceApplyRunModal('), context);

const createId = 'labvm-sea-cust12-20261125004430-932cee4dcf8946acadb752e36c58291d';
const removeId = 'labvm-rm-sea-cust12-20261125004430-932cee4dcf8946acadb752e36c58291d';
assert.equal(context.getLabVmAction({ runId: createId }), 'create');
assert.equal(context.getLabVmAction({ runId: removeId }), 'remove');
assert.equal(context.getLabVmAction({ runId: 'legacy-unknown' }), 'unknown');
context.renderLabVmHistory();
assert.equal(byId('labVmHistory').textContent, 'No recent runs.');
context._labVmRun = { runId: createId, status: 'Running', submittedDate: '2026-11-25T01:00:00Z' };
context.renderLabVmSummary();
assert.equal(byId('labVmStatusBadge').textContent, 'Create · Running');
context._labVmRemoveMode = true;
context.updateLabVmBadge({ runId: removeId, status: 'Completed', startedAt: '2026-11-25T02:00:00Z', submittedBy: '<user>', requests: [{ index: 1, os: 'Remove', targetServer: 'SEA-ER-01', status: 'Completed', message: '<result>' }] });
assert.equal(byId('labVmStatusBadge').textContent, 'Remove · Completed');
assert.equal(byId('labVmHistory').children.length, 1);
assert.equal(byId('labVmHistory').children[0].children[0].children[1].textContent.includes('<user>'), true);
byId('labVmHistory').children[0].children[1].click();
assert.ok(shown);
assert.match(byId('labVmHistoryOutput').textContent, /SEA-ER-01: Completed — <result>/);
assert.match(byId('labVmHistoryModalLabel').textContent, /Remove Lab VMs/);
for (let i = 0; i < 7; i++) context.updateLabVmBadge({ runId: 'test-' + i, status: 'Failed', startedAt: `2026-11-25T0${i}:00:00Z`, requests: [] });
assert.equal(context._labVmHistory.length, 5);
assert.equal(context._labVmHistory[0].runId, 'test-6');
assert.equal(context._labVmHistory[4].runId, 'test-2');

for (const [action, label] of [['deploy', 'Deploy to Azure'], ['remove', 'Remove from Azure'], ['provision', 'Provision at Provider'], ['deprovision', 'Deprovision at Provider'], [null, 'Unknown action']]) {
    runs.ServiceProviderInstructions = { jobId: 'job-1', action, status: 'Completed', isTerminal: true };
    context.renderAllAutomationSummaries();
    assert.equal(byId('automationStatusBadge-ServiceProviderInstructions').textContent, `${label} · Completed`);
    byId('automationReopenBtn-ServiceProviderInstructions').onclick();
    assert.deepEqual(Array.from(context.openArgs), ['ServiceProviderInstructions', 'job-1']);
}
console.log('Run history UI checks passed: actions, legacy IDs, live history, safe details, ordering, and provider summaries.');
