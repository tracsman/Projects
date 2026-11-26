'use strict';

// Offline modal checks; no network or VM operations are performed.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const view = fs.readFileSync(path.join(__dirname, '..', 'Views', 'Tenants', 'Config.cshtml'), 'utf8');
const start = view.indexOf('        // --- Lab VM Modal ---');
const end = view.indexOf('        // --- Async device run polling', start);
assert.ok(start >= 0 && end > start, 'Lab VM modal script not found');

function createHarness(terminalStatus) {
    const elements = new Map();
    const element = id => {
        if (!elements.has(id)) elements.set(id, {
            innerHTML: '', textContent: '', disabled: false, onclick: null,
            classList: { contains: () => true }, addEventListener() {}
        });
        return elements.get(id);
    };
    const responses = [
        { success: true, totalVms: 1, tenant: { tenantName: 'SEA-Cust12' }, servers: [{ server: 'SEA-ER-01', vms: [{ name: 'SEA-ER-12-VM01', state: 'Running' }] }] },
        { success: true, runId: 'remove-1', requests: [{ index: 1, targetServer: 'SEA-ER-01' }] },
        { success: true, status: terminalStatus, error: 'Removal failed', requests: [{ index: 1, status: terminalStatus, message: terminalStatus === 'Completed' ? 'VMs removed successfully' : 'Not elevated' }] }
    ];
    const context = {
        document: { getElementById: element, addEventListener() {} },
        bootstrap: { Modal: { getOrCreateInstance: () => ({ show() {} }) } },
        fetch: async () => ({ json: async () => { assert.ok(responses.length, 'Unexpected fetch'); return responses.shift(); } }),
        confirm: () => true, setSpinner() {}, getToken: () => 'test',
        renderLabVmSummary() {}, updateLabVmBadge() {}, _tenantGuid: 'test',
        setTimeout() { throw new Error('Unexpected continued polling'); }, clearTimeout() {}, console
    };
    vm.createContext(context);
    vm.runInContext(view.slice(start, end), context, { filename: 'Config.cshtml' });
    return { context, element, responses };
}

(async () => {
    for (const status of ['Completed', 'Failed']) {
        const { context, element, responses } = createHarness(status);
        await context.openLabVmRemoveModal();
        assert.equal(context._labVmRemoveMode, true);
        assert.equal(element('labVmRunBtn').onclick, context.submitLabVmRemove);
        assert.match(element('labVmTableBody').innerHTML, /Found \(State: Running\)/);
        await context.submitLabVmRemove();
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(responses.length, 0);
        assert.match(element('labVmRunBtn').innerHTML, /Remove All/);
        assert.doesNotMatch(element('labVmRunBtn').innerHTML, /Create VMs/);
        assert.equal(element('labVmRunBtn').disabled, true, 'A new removal requires a fresh scan');
        assert.equal(element('labVmRunBtn').onclick, context.submitLabVmRemove);
        assert.match(element('labVmRowStatus-1').innerHTML, status === 'Completed' ? /VMs removed successfully/ : /Not elevated/);
        assert.match(element('labVmStatus').innerHTML, status === 'Completed' ? /All VMs removed/ : /Removal failed.*scan before retrying/);
    }
    for (const isRemove of [true, false]) {
        const { context, element, responses } = createHarness('Failed');
        responses.splice(0, responses.length, {
            success: true, status: 'Failed', error: 'Test failure', tenant: { tenantName: 'SEA-Cust12' },
            requests: [{ index: 1, os: isRemove ? 'Remove' : 'Ubuntu', targetServer: 'SEA-ER-01', status: 'Failed', message: 'Test failure' }]
        });
        context._labVmRunId = 'running';
        context._labVmRemoveMode = isRemove;
        if (isRemove) await context.openLabVmModal();
        else await context.openLabVmRemoveModal();
        assert.equal(context._labVmRemoveMode, isRemove, 'Opening the other action must preserve the active run mode');
        assert.match(element('labVmModalLabel').textContent, isRemove ? /Remove Lab VMs/ : /Create Lab VMs/);
        assert.match(element('labVmRunBtn').innerHTML, isRemove ? /Remove All/ : /Create VMs/);
        assert.equal(element('labVmRunBtn').disabled, isRemove);
    }
    console.log('Lab VM removal modal checks passed: scan, submit, success, failure, and resume mode.');
})().catch(error => { console.error(error); process.exitCode = 1; });
