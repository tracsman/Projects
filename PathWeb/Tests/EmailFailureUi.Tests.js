'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const view = fs.readFileSync(path.join(__dirname, '..', 'Views', 'Tenants', 'Config.cshtml'), 'utf8');
const start = view.indexOf('        function sendNotificationEmail(configType) {');
const end = view.indexOf("        document.getElementById('automationModal')", start);
assert.ok(start >= 0 && end > start, 'Email send handler not found');

async function check(result, expectedHeading, expectedOutput) {
    const button = { innerHTML: 'Send Email', disabled: false };
    const state = { calls: 0 };
    const context = {
        event: { currentTarget: button },
        setSpinner(btn) { btn.disabled = true; },
        postAction() { state.calls++; return result instanceof Error ? Promise.reject(result) : Promise.resolve(result); },
        setEmailSendRun(run) { state.run = run; },
        showModal(title) { state.title = title; },
        setStatus(html) { state.status = html; },
        setOutputError(text) { state.output = text; }
    };
    vm.createContext(context);
    vm.runInContext(view.slice(start, end), context);
    context.sendNotificationEmail('eMailHTML');
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(state.title, 'Send Notification Email');
    assert.match(state.status, expectedHeading);
    assert.match(state.output, expectedOutput);
    assert.equal(state.calls, 1, 'Must not retry a side-effecting request');
    assert.equal(button.disabled, false);
}

(async () => {
    await check({ success: false, output: "API connection 'office365-2' needs reauthorization; check mailbox before retrying", emailRun: { status: 'Send Unconfirmed' } }, /delivery could not be confirmed/, /office365-2.*reauthorization/);
    await check({ success: false, output: 'Tenant has no contact email addresses configured.', emailRun: { status: 'Send Failed' } }, /Email send failed/, /no contact/);
    await check(new Error('network error'), /delivery could not be confirmed/, /mailbox before retrying/);
    console.log('Email failure modal checks passed: connector guidance, pre-send failure, network error, and no retry.');
})().catch(error => { console.error(error); process.exitCode = 1; });
