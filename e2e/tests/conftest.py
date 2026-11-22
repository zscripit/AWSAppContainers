"""Pruebas de extremo a extremo con Playwright contra la aplicación en ejecución.

BASE_URL apunta a la app (por defecto http://127.0.0.1:5000). BROWSER_CHANNEL permite usar,
por ejemplo, "msedge" en local; en CI se usa el Chromium que instala Playwright.
"""
import os
import re
import uuid
from pathlib import Path

import pytest
from playwright.sync_api import expect, sync_playwright

BASE_URL = os.environ.get("BASE_URL", "http://127.0.0.1:5000").rstrip("/")
PASSWORD = "Prueba123"
ROOT = Path(__file__).resolve().parent.parent

expect.set_options(timeout=10000)


@pytest.fixture(scope="session")
def browser():
    with sync_playwright() as playwright:
        channel = os.environ.get("BROWSER_CHANNEL") or None
        browser = playwright.chromium.launch(channel=channel, headless=True)
        yield browser
        browser.close()


@pytest.fixture
def context(browser):
    context = browser.new_context(base_url=BASE_URL, viewport={"width": 1280, "height": 900})
    yield context
    context.close()


@pytest.fixture
def page(context, request):
    page = context.new_page()
    yield page
    if getattr(request.node, "rep_call", None) and request.node.rep_call.failed:
        evidence = ROOT / "artifacts" / "screenshots"
        evidence.mkdir(parents=True, exist_ok=True)
        name = re.sub(r"[^\w.-]", "_", request.node.nodeid.split("::")[-1])
        page.screenshot(path=str(evidence / f"{name}.png"), full_page=True)


@pytest.fixture
def api(context):
    return context.request


def unique_email(prefix="qa"):
    return f"{prefix}-{uuid.uuid4().hex[:10]}@example.com"


@pytest.fixture
def user(api):
    """Usuario recién registrado vía API: {id, nombre, email, password}."""
    email = unique_email()
    resp = api.post("/api/auth/registro", data={"nombre": "QA Tester", "email": email, "password": PASSWORD})
    assert resp.status == 201, resp.text()
    data = resp.json()["data"][0]
    return {**data, "password": PASSWORD}


@pytest.fixture
def token(api, user):
    resp = api.post("/api/auth/login", data={"email": user["email"], "password": PASSWORD})
    return resp.json()["data"][0]["token"]


@pytest.fixture
def auth_headers(token):
    return {"Authorization": f"Bearer {token}"}


@pytest.fixture
def logged_page(page, user):
    """Página con la sesión iniciada desde el formulario de login."""
    page.goto("/login.html")
    page.get_by_test_id("email").fill(user["email"])
    page.get_by_test_id("password").fill(PASSWORD)
    page.get_by_test_id("submit").click()
    expect(page).to_have_url(re.compile("/usuarios.html"))
    return page


@pytest.hookimpl(hookwrapper=True, tryfirst=True)
def pytest_runtest_makereport(item, call):
    outcome = yield
    setattr(item, f"rep_{outcome.get_result().when}", outcome.get_result())
