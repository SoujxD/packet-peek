"""Optional real-browser verification against an isolated Compose test stack.

Run prepare before enabling replay, then verify after restarting the app with replay.
Requires Playwright 1.56.0 and its Chromium browser.
"""
import os
from pathlib import Path
import re
import sys
from playwright.sync_api import sync_playwright, expect

base = os.environ['PACKETPEEK_URL'].rstrip('/')
mode = sys.argv[1]
output = Path('build/ui')
output.mkdir(parents=True, exist_ok=True)
with sync_playwright() as playwright:
    browser = playwright.chromium.launch()
    page = browser.new_page(viewport={'width': 1440, 'height': 1000})
    page.goto(base + '/rules')
    page.get_by_label('Username', exact=True).fill(os.environ['OPERATOR_USERNAME'])
    page.get_by_label('Password', exact=True).fill(os.environ['OPERATOR_PASSWORD'])
    page.get_by_role('button', name='Sign in', exact=True).click()
    expect(page.get_by_text('Signed in as', exact=False)).to_be_visible(timeout=30000)
    if mode == 'prepare':
        page.get_by_label('Name', exact=True).fill('Browser CRUD check')
        page.get_by_label('Threshold', exact=True).fill('1')
        page.get_by_role('button', name='Create rule', exact=True).click()
        row = page.get_by_role('row').filter(has_text='Browser CRUD check')
        expect(row).to_have_count(1)
        row.get_by_role('button', name='Edit', exact=True).click()
        page.get_by_label('Name', exact=True).fill('Browser edited rule')
        page.get_by_role('button', name='Save changes', exact=True).click()
        row = page.get_by_role('row').filter(has_text='Browser edited rule')
        expect(row).to_have_count(1)
        row.get_by_role('button', name='Delete', exact=True).click()
        expect(row).to_have_count(0)
        page.get_by_label('Name', exact=True).fill('Browser replay rule')
        page.get_by_label('Threshold', exact=True).fill('1')
        page.get_by_role('button', name='Create rule', exact=True).click()
        expect(page.get_by_role('row').filter(has_text='Browser replay rule')).to_have_count(1)
        page.screenshot(path=str(output / 'rules.png'), full_page=True)
        print('PASS: browser login, rule create/edit/delete, replay rule setup')
    elif mode == 'verify':
        page.get_by_role('link', name='Flows', exact=True).click()
        expect(page.get_by_role('heading', name='Closed flows', exact=True)).to_be_visible()
        expect(page.get_by_role('row').filter(has_text='192.0.2.1:12345')).to_have_count(2, timeout=30000)
        page.screenshot(path=str(output / 'flows.png'), full_page=True)
        page.get_by_role('link', name='Alerts', exact=True).click()
        expect(page.get_by_role('row').filter(has_text='Browser replay rule')).to_have_count(1)
        page.get_by_role('button', name='Inspect', exact=True).click()
        expect(page.get_by_role('heading', name=re.compile(r'^Alert \d+$'))).to_be_visible()
        expect(page.get_by_text('windowSeconds', exact=True)).to_be_visible()
        page.screenshot(path=str(output / 'alerts.png'), full_page=True)
        page.get_by_role('button', name='Close', exact=True).click()
        page.get_by_role('button', name='Sign out', exact=True).click()
        expect(page.get_by_role('heading', name='Operator sign in', exact=True)).to_be_visible()
        print('PASS: browser navigation, two flows, one alert, drill-down, sign out')
    else:
        raise ValueError('Expected prepare or verify')
    browser.close()
