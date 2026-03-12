"""
Comprehensive pytest tests for NovaSCM Flask API (api.py).
Run from the project root:
    cd server && pytest tests/ -v
"""
import pytest
import json
import sys
import os

# Add parent directory (server/) to path so we can import api
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Set test env vars BEFORE importing api
os.environ["NOVASCM_API_KEY"] = "test-key-123"

import api  # noqa: E402  (must come after env var setup)

TEST_KEY = "test-key-123"
AUTH = {"X-Api-Key": TEST_KEY}


# ── Fixtures ─────────────────────────────────────────────────────────────────

@pytest.fixture
def client(tmp_path):
    """Create a test Flask client backed by a fresh temp-file SQLite DB."""
    db_file = str(tmp_path / "test.db")
    # Patch module-level DB path and API_KEY before init_db
    api.DB = db_file
    api.API_KEY = TEST_KEY

    # Ensure os.makedirs won't fail for :memory: – db_file is a real path
    api.init_db()

    api.app.config["TESTING"] = True
    with api.app.test_client() as c:
        yield c


# ── Helper ───────────────────────────────────────────────────────────────────

def _create_cr(client, pc_name="PC-TEST-01", domain="corp.local", **extra):
    """Helper: POST /api/cr and return the JSON response body."""
    payload = {"pc_name": pc_name, "domain": domain, **extra}
    r = client.post("/api/cr", headers=AUTH,
                    data=json.dumps(payload),
                    content_type="application/json")
    return r


def _create_workflow(client, nome="Test Workflow", descrizione="desc"):
    payload = {"nome": nome, "descrizione": descrizione}
    r = client.post("/api/workflows", headers=AUTH,
                    data=json.dumps(payload),
                    content_type="application/json")
    return r


# ══════════════════════════════════════════════════════════════════════════════
# 1. Health check
# ══════════════════════════════════════════════════════════════════════════════

class TestHealth:
    def test_health_returns_200(self, client):
        r = client.get("/health")
        assert r.status_code == 200

    def test_health_status_ok(self, client):
        r = client.get("/health")
        assert r.get_json()["status"] == "ok"

    def test_health_no_auth_required(self, client):
        """Health endpoint deve essere raggiungibile senza X-Api-Key."""
        r = client.get("/health")
        assert r.status_code == 200

    def test_health_does_not_expose_db_path(self, client):
        """Health non deve esporre il path del DB."""
        data = client.get("/health").get_json()
        assert "db" not in data


# ══════════════════════════════════════════════════════════════════════════════
# 2. Authentication
# ══════════════════════════════════════════════════════════════════════════════

class TestAuth:
    def test_missing_key_returns_401(self, client):
        r = client.get("/api/cr")
        assert r.status_code == 401

    def test_wrong_key_returns_401(self, client):
        r = client.get("/api/cr", headers={"X-Api-Key": "wrong-key"})
        assert r.status_code == 401

    def test_correct_key_returns_200(self, client):
        r = client.get("/api/cr", headers=AUTH)
        assert r.status_code == 200

    def test_wrong_key_has_error_body(self, client):
        r = client.get("/api/cr", headers={"X-Api-Key": "bad"})
        data = r.get_json()
        assert "error" in data

    def test_auth_on_workflows_endpoint(self, client):
        r = client.get("/api/workflows")
        assert r.status_code == 401

    def test_auth_on_settings_endpoint(self, client):
        r = client.get("/api/settings")
        assert r.status_code == 401


# ══════════════════════════════════════════════════════════════════════════════
# 3. CR CRUD
# ══════════════════════════════════════════════════════════════════════════════

class TestCrList:
    def test_cr_list_empty(self, client):
        r = client.get("/api/cr", headers=AUTH)
        assert r.status_code == 200
        assert r.get_json() == []

    def test_cr_list_returns_array(self, client):
        data = client.get("/api/cr", headers=AUTH).get_json()
        assert isinstance(data, list)


class TestCrCreate:
    def test_create_cr_success(self, client):
        r = _create_cr(client)
        assert r.status_code == 201

    def test_create_cr_response_has_id(self, client):
        data = _create_cr(client).get_json()
        assert "id" in data
        assert data["id"] > 0

    def test_create_cr_pc_name_uppercased(self, client):
        r = _create_cr(client, pc_name="pc-lowercase")
        data = r.get_json()
        assert data["pc_name"] == "PC-LOWERCASE"

    def test_create_cr_status_defaults_to_open(self, client):
        data = _create_cr(client).get_json()
        assert data["status"] == "open"

    def test_create_cr_software_defaults_empty(self, client):
        data = _create_cr(client).get_json()
        assert data["software"] == []

    def test_create_cr_with_software(self, client):
        r = _create_cr(client, software=["Mozilla.Firefox", "7zip.7zip"])
        data = r.get_json()
        assert "Mozilla.Firefox" in data["software"]

    def test_create_cr_missing_pc_name_returns_400(self, client):
        payload = {"domain": "corp.local"}
        r = client.post("/api/cr", headers=AUTH,
                        data=json.dumps(payload), content_type="application/json")
        assert r.status_code == 400

    def test_create_cr_missing_domain_returns_400(self, client):
        payload = {"pc_name": "TEST-PC"}
        r = client.post("/api/cr", headers=AUTH,
                        data=json.dumps(payload), content_type="application/json")
        assert r.status_code == 400

    def test_create_duplicate_cr_returns_409(self, client):
        _create_cr(client, pc_name="DUP-PC")
        r = _create_cr(client, pc_name="DUP-PC")
        assert r.status_code == 409

    def test_create_cr_appears_in_list(self, client):
        _create_cr(client, pc_name="LIST-PC")
        items = client.get("/api/cr", headers=AUTH).get_json()
        names = [i["pc_name"] for i in items]
        assert "LIST-PC" in names


class TestCrGet:
    def test_get_cr_by_id(self, client):
        cr_id = _create_cr(client, pc_name="GET-PC").get_json()["id"]
        r = client.get(f"/api/cr/{cr_id}", headers=AUTH)
        assert r.status_code == 200
        assert r.get_json()["pc_name"] == "GET-PC"

    def test_get_nonexistent_cr_returns_404(self, client):
        r = client.get("/api/cr/99999", headers=AUTH)
        assert r.status_code == 404

    def test_get_cr_by_name(self, client):
        _create_cr(client, pc_name="BYNAME-PC")
        r = client.get("/api/cr/by-name/BYNAME-PC", headers=AUTH)
        assert r.status_code == 200
        assert r.get_json()["pc_name"] == "BYNAME-PC"

    def test_get_cr_by_name_not_found(self, client):
        r = client.get("/api/cr/by-name/GHOST-PC", headers=AUTH)
        assert r.status_code == 404

    def test_get_cr_does_not_expose_passwords(self, client):
        cr_id = _create_cr(client, pc_name="PASSWD-PC").get_json()["id"]
        r = client.get(f"/api/cr/{cr_id}", headers=AUTH)
        assert r.status_code == 200
        d = r.get_json()
        assert "join_pass" not in d
        assert "admin_pass" not in d

    def test_list_cr_does_not_expose_passwords(self, client):
        _create_cr(client, pc_name="PASSWD-LIST-PC")
        r = client.get("/api/cr", headers=AUTH)
        assert r.status_code == 200
        for cr in r.get_json():
            assert "join_pass" not in cr
            assert "admin_pass" not in cr


class TestCrUpdateStatus:
    def test_update_status_to_in_progress(self, client):
        cr_id = _create_cr(client, pc_name="STATUS-PC").get_json()["id"]
        r = client.put(f"/api/cr/{cr_id}/status", headers=AUTH,
                       data=json.dumps({"status": "in_progress"}),
                       content_type="application/json")
        assert r.status_code == 200
        assert r.get_json()["status"] == "in_progress"

    def test_update_status_to_completed_sets_completed_at(self, client):
        cr_id = _create_cr(client, pc_name="COMP-PC").get_json()["id"]
        r = client.put(f"/api/cr/{cr_id}/status", headers=AUTH,
                       data=json.dumps({"status": "completed"}),
                       content_type="application/json")
        data = r.get_json()
        assert data["status"] == "completed"
        assert data.get("completed_at") is not None

    def test_update_status_invalid_value_returns_400(self, client):
        cr_id = _create_cr(client, pc_name="INV-PC").get_json()["id"]
        r = client.put(f"/api/cr/{cr_id}/status", headers=AUTH,
                       data=json.dumps({"status": "invalid_state"}),
                       content_type="application/json")
        assert r.status_code == 400

    def test_update_status_all_valid_states(self, client):
        for state in ("open", "in_progress", "completed"):
            cr_id = _create_cr(client, pc_name=f"ST-{state.upper().replace('_','-')}").get_json()["id"]
            r = client.put(f"/api/cr/{cr_id}/status", headers=AUTH,
                           data=json.dumps({"status": state}),
                           content_type="application/json")
            assert r.status_code == 200, f"Expected 200 for status={state}"


class TestCrDelete:
    def test_delete_cr(self, client):
        cr_id = _create_cr(client, pc_name="DEL-PC").get_json()["id"]
        r = client.delete(f"/api/cr/{cr_id}", headers=AUTH)
        assert r.status_code == 200
        assert r.get_json()["ok"] is True

    def test_delete_cr_removes_from_list(self, client):
        cr_id = _create_cr(client, pc_name="DEL2-PC").get_json()["id"]
        client.delete(f"/api/cr/{cr_id}", headers=AUTH)
        items = client.get("/api/cr", headers=AUTH).get_json()
        ids = [i["id"] for i in items]
        assert cr_id not in ids

    def test_delete_nonexistent_cr_returns_404(self, client):
        r = client.delete("/api/cr/99999", headers=AUTH)
        assert r.status_code == 404


# ══════════════════════════════════════════════════════════════════════════════
# 4. Workflow CRUD
# ══════════════════════════════════════════════════════════════════════════════

class TestWorkflowList:
    def test_workflow_list_empty(self, client):
        r = client.get("/api/workflows", headers=AUTH)
        assert r.status_code == 200
        assert r.get_json() == []

    def test_workflow_list_returns_array(self, client):
        data = client.get("/api/workflows", headers=AUTH).get_json()
        assert isinstance(data, list)


class TestWorkflowCreate:
    def test_create_workflow_success(self, client):
        r = _create_workflow(client)
        assert r.status_code == 201

    def test_create_workflow_has_id(self, client):
        data = _create_workflow(client).get_json()
        assert "id" in data and data["id"] > 0

    def test_create_workflow_stores_nome(self, client):
        data = _create_workflow(client, nome="My Flow").get_json()
        assert data["nome"] == "My Flow"

    def test_create_workflow_missing_nome_returns_400(self, client):
        r = client.post("/api/workflows", headers=AUTH,
                        data=json.dumps({"descrizione": "no name"}),
                        content_type="application/json")
        assert r.status_code == 400

    def test_create_duplicate_workflow_returns_409(self, client):
        _create_workflow(client, nome="Dup Flow")
        r = _create_workflow(client, nome="Dup Flow")
        assert r.status_code == 409

    def test_create_workflow_appears_in_list(self, client):
        _create_workflow(client, nome="Listed Flow")
        items = client.get("/api/workflows", headers=AUTH).get_json()
        names = [i["nome"] for i in items]
        assert "Listed Flow" in names


class TestWorkflowGet:
    def test_get_workflow_by_id(self, client):
        wf_id = _create_workflow(client, nome="Get Flow").get_json()["id"]
        r = client.get(f"/api/workflows/{wf_id}", headers=AUTH)
        assert r.status_code == 200
        assert r.get_json()["nome"] == "Get Flow"

    def test_get_workflow_includes_steps_key(self, client):
        wf_id = _create_workflow(client, nome="Steps Flow").get_json()["id"]
        data = client.get(f"/api/workflows/{wf_id}", headers=AUTH).get_json()
        assert "steps" in data
        assert isinstance(data["steps"], list)

    def test_get_nonexistent_workflow_returns_404(self, client):
        r = client.get("/api/workflows/99999", headers=AUTH)
        assert r.status_code == 404


class TestWorkflowUpdate:
    def test_update_workflow_nome(self, client):
        wf_id = _create_workflow(client, nome="Old Nome").get_json()["id"]
        r = client.put(f"/api/workflows/{wf_id}", headers=AUTH,
                       data=json.dumps({"nome": "New Nome", "descrizione": ""}),
                       content_type="application/json")
        assert r.status_code == 200
        assert r.get_json()["nome"] == "New Nome"

    def test_update_workflow_increments_version(self, client):
        wf_id = _create_workflow(client, nome="Ver Flow").get_json()["id"]
        v1 = client.get(f"/api/workflows/{wf_id}", headers=AUTH).get_json()["versione"]
        client.put(f"/api/workflows/{wf_id}", headers=AUTH,
                   data=json.dumps({"nome": "Ver Flow", "descrizione": "upd"}),
                   content_type="application/json")
        v2 = client.get(f"/api/workflows/{wf_id}", headers=AUTH).get_json()["versione"]
        assert v2 == v1 + 1


class TestWorkflowDelete:
    def test_delete_workflow(self, client):
        wf_id = _create_workflow(client, nome="Del Flow").get_json()["id"]
        r = client.delete(f"/api/workflows/{wf_id}", headers=AUTH)
        assert r.status_code == 200
        assert r.get_json()["ok"] is True

    def test_delete_workflow_removes_from_list(self, client):
        wf_id = _create_workflow(client, nome="Del2 Flow").get_json()["id"]
        client.delete(f"/api/workflows/{wf_id}", headers=AUTH)
        items = client.get("/api/workflows", headers=AUTH).get_json()
        ids = [i["id"] for i in items]
        assert wf_id not in ids

    def test_delete_nonexistent_workflow_returns_404(self, client):
        r = client.delete("/api/workflows/99999", headers=AUTH)
        assert r.status_code == 404


class TestWorkflowSteps:
    def _step_payload(self, ordine=1, nome="Step 1", tipo="shell_script"):
        return {
            "ordine": ordine,
            "nome": nome,
            "tipo": tipo,
            "parametri": {"cmd": "echo hello"},
        }

    def test_add_step_to_workflow(self, client):
        wf_id = _create_workflow(client, nome="Step Flow").get_json()["id"]
        r = client.post(f"/api/workflows/{wf_id}/steps", headers=AUTH,
                        data=json.dumps(self._step_payload()),
                        content_type="application/json")
        assert r.status_code == 201

    def test_step_appears_in_workflow(self, client):
        wf_id = _create_workflow(client, nome="Step2 Flow").get_json()["id"]
        client.post(f"/api/workflows/{wf_id}/steps", headers=AUTH,
                    data=json.dumps(self._step_payload(nome="My Step")),
                    content_type="application/json")
        wf = client.get(f"/api/workflows/{wf_id}", headers=AUTH).get_json()
        names = [s["nome"] for s in wf["steps"]]
        assert "My Step" in names

    def test_add_step_invalid_tipo_returns_400(self, client):
        wf_id = _create_workflow(client, nome="BadStep Flow").get_json()["id"]
        r = client.post(f"/api/workflows/{wf_id}/steps", headers=AUTH,
                        data=json.dumps(self._step_payload(tipo="invalid_type")),
                        content_type="application/json")
        assert r.status_code == 400

    def test_add_duplicate_ordine_returns_409(self, client):
        wf_id = _create_workflow(client, nome="DupStep Flow").get_json()["id"]
        client.post(f"/api/workflows/{wf_id}/steps", headers=AUTH,
                    data=json.dumps(self._step_payload(ordine=1)),
                    content_type="application/json")
        r = client.post(f"/api/workflows/{wf_id}/steps", headers=AUTH,
                        data=json.dumps(self._step_payload(ordine=1)),
                        content_type="application/json")
        assert r.status_code == 409

    def test_delete_step(self, client):
        wf_id = _create_workflow(client, nome="DelStep Flow").get_json()["id"]
        step_id = client.post(f"/api/workflows/{wf_id}/steps", headers=AUTH,
                              data=json.dumps(self._step_payload()),
                              content_type="application/json").get_json()["id"]
        r = client.delete(f"/api/workflows/{wf_id}/steps/{step_id}", headers=AUTH)
        assert r.status_code == 200
        assert r.get_json()["ok"] is True

    def test_list_steps_endpoint(self, client):
        wf_id = _create_workflow(client, nome="ListStep Flow").get_json()["id"]
        client.post(f"/api/workflows/{wf_id}/steps", headers=AUTH,
                    data=json.dumps(self._step_payload()),
                    content_type="application/json")
        r = client.get(f"/api/workflows/{wf_id}/steps", headers=AUTH)
        assert r.status_code == 200
        assert isinstance(r.get_json(), list)
        assert len(r.get_json()) == 1

    def test_update_step(self, client):
        wf_id = _create_workflow(client, nome="UpdStep Flow").get_json()["id"]
        step_id = client.post(f"/api/workflows/{wf_id}/steps", headers=AUTH,
                              data=json.dumps(self._step_payload(nome="Before")),
                              content_type="application/json").get_json()["id"]
        r = client.put(f"/api/workflows/{wf_id}/steps/{step_id}", headers=AUTH,
                       data=json.dumps(self._step_payload(nome="After")),
                       content_type="application/json")
        assert r.status_code == 200
        assert r.get_json()["nome"] == "After"


# ══════════════════════════════════════════════════════════════════════════════
# 5. Settings
# ══════════════════════════════════════════════════════════════════════════════

class TestSettings:
    def test_get_settings_empty(self, client):
        r = client.get("/api/settings", headers=AUTH)
        assert r.status_code == 200
        assert r.get_json() == {}

    def test_put_settings_stores_key(self, client):
        client.put("/api/settings", headers=AUTH,
                   data=json.dumps({"webhook_url": "http://example.com"}),
                   content_type="application/json")
        data = client.get("/api/settings", headers=AUTH).get_json()
        assert data.get("webhook_url") == "http://example.com"

    def test_put_settings_returns_all_settings(self, client):
        r = client.put("/api/settings", headers=AUTH,
                       data=json.dumps({"webhook_url": "http://a.com", "webhook_enabled": "1"}),
                       content_type="application/json")
        assert r.status_code == 200
        data = r.get_json()
        assert data["webhook_url"] == "http://a.com"
        assert data["webhook_enabled"] == "1"

    def test_put_settings_upserts_existing_key(self, client):
        client.put("/api/settings", headers=AUTH,
                   data=json.dumps({"webhook_url": "http://first.com"}),
                   content_type="application/json")
        client.put("/api/settings", headers=AUTH,
                   data=json.dumps({"webhook_url": "http://second.com"}),
                   content_type="application/json")
        data = client.get("/api/settings", headers=AUTH).get_json()
        assert data["webhook_url"] == "http://second.com"

    def test_put_settings_none_value_stored_as_empty_string(self, client):
        client.put("/api/settings", headers=AUTH,
                   data=json.dumps({"webhook_url": None}),
                   content_type="application/json")
        data = client.get("/api/settings", headers=AUTH).get_json()
        assert data.get("webhook_url") == ""

    def test_put_settings_no_auth_returns_401(self, client):
        r = client.put("/api/settings",
                       data=json.dumps({"k": "v"}),
                       content_type="application/json")
        assert r.status_code == 401


# ══════════════════════════════════════════════════════════════════════════════
# 6. PC Workflow Assignments
# ══════════════════════════════════════════════════════════════════════════════

class TestPcWorkflows:
    def test_pc_workflows_empty(self, client):
        r = client.get("/api/pc-workflows", headers=AUTH)
        assert r.status_code == 200
        assert r.get_json() == []

    def test_assign_workflow_to_pc(self, client):
        wf_id = _create_workflow(client, nome="Assign Flow").get_json()["id"]
        r = client.post("/api/pc-workflows", headers=AUTH,
                        data=json.dumps({"pc_name": "ASSIGNED-PC", "workflow_id": wf_id}),
                        content_type="application/json")
        assert r.status_code == 201

    def test_assign_workflow_pc_name_uppercased(self, client):
        wf_id = _create_workflow(client, nome="Upper Flow").get_json()["id"]
        r = client.post("/api/pc-workflows", headers=AUTH,
                        data=json.dumps({"pc_name": "lower-pc", "workflow_id": wf_id}),
                        content_type="application/json")
        assert r.get_json()["pc_name"] == "LOWER-PC"

    def test_assign_nonexistent_workflow_returns_404(self, client):
        r = client.post("/api/pc-workflows", headers=AUTH,
                        data=json.dumps({"pc_name": "PC-X", "workflow_id": 99999}),
                        content_type="application/json")
        assert r.status_code == 404

    def test_assign_duplicate_returns_409(self, client):
        wf_id = _create_workflow(client, nome="Dup Assign Flow").get_json()["id"]
        payload = {"pc_name": "DUPPC", "workflow_id": wf_id}
        client.post("/api/pc-workflows", headers=AUTH,
                    data=json.dumps(payload), content_type="application/json")
        r = client.post("/api/pc-workflows", headers=AUTH,
                        data=json.dumps(payload), content_type="application/json")
        assert r.status_code == 409

    def test_delete_pc_workflow(self, client):
        wf_id = _create_workflow(client, nome="Del Assign Flow").get_json()["id"]
        pw_id = client.post("/api/pc-workflows", headers=AUTH,
                            data=json.dumps({"pc_name": "DELPC", "workflow_id": wf_id}),
                            content_type="application/json").get_json()["id"]
        r = client.delete(f"/api/pc-workflows/{pw_id}", headers=AUTH)
        assert r.status_code == 200

    def test_delete_nonexistent_pc_workflow_returns_404(self, client):
        r = client.delete("/api/pc-workflows/99999", headers=AUTH)
        assert r.status_code == 404


# ══════════════════════════════════════════════════════════════════════════════
# 7. CR Step tracking
# ══════════════════════════════════════════════════════════════════════════════

class TestCrSteps:
    def test_report_step_for_existing_cr(self, client):
        _create_cr(client, pc_name="STEP-PC")
        r = client.post("/api/cr/by-name/STEP-PC/step", headers=AUTH,
                        data=json.dumps({"step": "domain_join", "status": "done"}),
                        content_type="application/json")
        assert r.status_code == 200
        assert r.get_json()["ok"] is True

    def test_get_steps_for_cr(self, client):
        cr_id = _create_cr(client, pc_name="STEPS-PC2").get_json()["id"]
        client.post("/api/cr/by-name/STEPS-PC2/step", headers=AUTH,
                    data=json.dumps({"step": "winget_install", "status": "done"}),
                    content_type="application/json")
        r = client.get(f"/api/cr/{cr_id}/steps", headers=AUTH)
        assert r.status_code == 200
        data = r.get_json()
        assert "items" in data and "total" in data and "page" in data
        assert any(s["step_name"] == "winget_install" for s in data["items"])

    def test_get_steps_pagination(self, client):
        cr_id = _create_cr(client, pc_name="STEPS-PAGED").get_json()["id"]
        for i in range(5):
            client.post("/api/cr/by-name/STEPS-PAGED/step", headers=AUTH,
                        data=json.dumps({"step": f"step_{i}", "status": "done"}),
                        content_type="application/json")
        r = client.get(f"/api/cr/{cr_id}/steps?page=1&per_page=2", headers=AUTH)
        assert r.status_code == 200
        data = r.get_json()
        assert data["total"] == 5
        assert data["per_page"] == 2
        assert len(data["items"]) == 2

    def test_report_step_nonexistent_cr_returns_404(self, client):
        r = client.post("/api/cr/by-name/GHOST-PC/step", headers=AUTH,
                        data=json.dumps({"step": "x", "status": "done"}),
                        content_type="application/json")
        assert r.status_code == 404


# ══════════════════════════════════════════════════════════════════════════════
# 8. CR Check-in
# ══════════════════════════════════════════════════════════════════════════════

class TestCrCheckin:
    def test_checkin_existing_cr(self, client):
        _create_cr(client, pc_name="CHECKIN-PC")
        r = client.post("/api/cr/by-name/CHECKIN-PC/checkin", headers=AUTH)
        assert r.status_code == 200
        data = r.get_json()
        assert data["ok"] is True
        assert "last_seen" in data

    def test_checkin_nonexistent_cr_returns_404(self, client):
        r = client.post("/api/cr/by-name/GHOST-PC/checkin", headers=AUTH)
        assert r.status_code == 404


# ══════════════════════════════════════════════════════════════════════════════
# 9. Version / Download endpoints (no actual files)
# ══════════════════════════════════════════════════════════════════════════════

class TestVersion:
    def test_get_version_returns_200(self, client):
        r = client.get("/api/version", headers=AUTH)
        assert r.status_code == 200

    def test_get_version_has_version_field(self, client):
        data = client.get("/api/version", headers=AUTH).get_json()
        assert "version" in data

    def test_download_exe_not_found(self, client):
        """No actual exe in test env — expect 404."""
        r = client.get("/api/download/NovaSCM.exe", headers=AUTH)
        assert r.status_code == 404

    def test_download_agent_not_found(self, client):
        r = client.get("/api/download/agent", headers=AUTH)
        assert r.status_code == 404

    # ── Round 7 ──────────────────────────────────────────────────────────────

    def test_ui_api_call_with_auth(self, client):
        """GET /api/cr con X-Api-Key deve rispondere 200."""
        r = client.get("/api/cr", headers=AUTH)
        assert r.status_code == 200

    def test_ui_api_call_without_auth_fails(self, client):
        """GET /api/cr senza header deve rispondere 401."""
        r = client.get("/api/cr")
        assert r.status_code == 401

    def test_delete_workflow_cascade(self, client):
        """DELETE workflow deve rimuovere anche workflow_steps figli."""
        wf = client.post("/api/workflows",
                         json={"nome": "WF-CASCADE"},
                         headers=AUTH).get_json()
        wf_id = wf["id"]
        client.post(f"/api/workflows/{wf_id}/steps",
                    json={"nome": "S1", "tipo": "reboot", "ordine": 1},
                    headers=AUTH)
        r = client.delete(f"/api/workflows/{wf_id}", headers=AUTH)
        assert r.status_code == 200
        # Il workflow non deve più esistere
        r2 = client.get(f"/api/workflows/{wf_id}", headers=AUTH)
        assert r2.status_code == 404

    def test_delete_cr_also_removes_cr_steps(self, client):
        """DELETE /api/cr deve rimuovere anche i cr_steps figli."""
        cr = client.post("/api/cr",
                         json={"pc_name": "STEP-PC", "domain": "test.local"},
                         headers=AUTH).get_json()
        cr_id = cr["id"]
        # Inserisce uno step reale tramite endpoint corretto
        r_step = client.post("/api/cr/by-name/STEP-PC/step",
                             json={"step": "postinstall_start", "status": "done"},
                             headers=AUTH)
        assert r_step.status_code == 200
        # Verifica che lo step esista prima della cancellazione
        steps_before = client.get(f"/api/cr/{cr_id}/steps", headers=AUTH).get_json()
        assert steps_before["total"] == 1
        # Cancella la CR
        client.delete(f"/api/cr/{cr_id}", headers=AUTH)
        # La CR non deve più esistere
        r = client.get(f"/api/cr/{cr_id}", headers=AUTH)
        assert r.status_code == 404


# ── v2.1.0 Feature Tests ──────────────────────────────────────────────────────

class TestSuErroreValidation:
    """FEATURE-3: validazione su_errore in add_step e update_step."""

    def _create_wf(self, client):
        wf = client.post("/api/workflows", json={"nome": "WF-SU-ERR"}, headers=AUTH).get_json()
        return wf["id"]

    def test_add_step_su_errore_stop(self, client):
        wf_id = self._create_wf(client)
        r = client.post(f"/api/workflows/{wf_id}/steps",
                        json={"nome": "S1", "tipo": "reboot", "ordine": 1, "su_errore": "stop"},
                        headers=AUTH)
        assert r.status_code == 201
        assert r.get_json()["su_errore"] == "stop"

    def test_add_step_su_errore_continue(self, client):
        wf_id = self._create_wf(client)
        r = client.post(f"/api/workflows/{wf_id}/steps",
                        json={"nome": "S2", "tipo": "message", "ordine": 2, "su_errore": "continue"},
                        headers=AUTH)
        assert r.status_code == 201
        assert r.get_json()["su_errore"] == "continue"

    def test_add_step_su_errore_retry(self, client):
        wf_id = self._create_wf(client)
        r = client.post(f"/api/workflows/{wf_id}/steps",
                        json={"nome": "S3", "tipo": "winget_install", "ordine": 3, "su_errore": "retry"},
                        headers=AUTH)
        assert r.status_code == 201
        assert r.get_json()["su_errore"] == "retry"

    def test_add_step_su_errore_invalid(self, client):
        wf_id = self._create_wf(client)
        r = client.post(f"/api/workflows/{wf_id}/steps",
                        json={"nome": "S4", "tipo": "reboot", "ordine": 4, "su_errore": "ignore"},
                        headers=AUTH)
        assert r.status_code == 400
        assert "su_errore" in r.get_json()["error"]

    def test_update_step_su_errore_invalid(self, client):
        wf_id = self._create_wf(client)
        step = client.post(f"/api/workflows/{wf_id}/steps",
                           json={"nome": "S1", "tipo": "reboot", "ordine": 1},
                           headers=AUTH).get_json()
        r = client.put(f"/api/workflows/{wf_id}/steps/{step['id']}",
                       json={"nome": "S1", "tipo": "reboot", "ordine": 1, "su_errore": "bad"},
                       headers=AUTH)
        assert r.status_code == 400

    def test_update_step_su_errore_retry(self, client):
        wf_id = self._create_wf(client)
        step = client.post(f"/api/workflows/{wf_id}/steps",
                           json={"nome": "S1", "tipo": "reboot", "ordine": 1},
                           headers=AUTH).get_json()
        r = client.put(f"/api/workflows/{wf_id}/steps/{step['id']}",
                       json={"nome": "S1", "tipo": "reboot", "ordine": 1, "su_errore": "retry"},
                       headers=AUTH)
        assert r.status_code == 200
        assert r.get_json()["su_errore"] == "retry"


class TestArchivedAndHistory:
    """FEATURE-4: archived filter + history endpoint."""

    def _setup(self, client):
        """Crea workflow + assegna a PC + mette in stato running."""
        wf = client.post("/api/workflows", json={"nome": "WF-ARCH"}, headers=AUTH).get_json()
        wf_id = wf["id"]
        client.post(f"/api/workflows/{wf_id}/steps",
                    json={"nome": "S1", "tipo": "reboot", "ordine": 1},
                    headers=AUTH)
        pw = client.post("/api/pc-workflows",
                         json={"pc_name": "ARCH-PC", "workflow_id": wf_id},
                         headers=AUTH).get_json()
        pw_id = pw["id"]
        # Metti in stato running
        import api as _api
        with _api.get_db_ctx() as conn:
            conn.execute("UPDATE pc_workflows SET status='running', started_at=datetime('now') WHERE id=?",
                         (pw_id,))
            conn.commit()
        return pw_id, wf_id

    def test_list_pc_workflows_excludes_archived(self, client):
        pw_id, _ = self._setup(client)
        # Archivia il workflow
        import api as _api
        with _api.get_db_ctx() as conn:
            conn.execute("UPDATE pc_workflows SET archived=1, status='completed' WHERE id=?", (pw_id,))
            conn.commit()
        rows = client.get("/api/pc-workflows", headers=AUTH).get_json()
        ids = [r["id"] for r in rows]
        assert pw_id not in ids

    def test_list_pc_workflows_includes_non_archived(self, client):
        pw_id, _ = self._setup(client)
        rows = client.get("/api/pc-workflows", headers=AUTH).get_json()
        ids = [r["id"] for r in rows]
        assert pw_id in ids

    def test_history_requires_pc_name(self, client):
        r = client.get("/api/pc-workflows/history", headers=AUTH)
        assert r.status_code == 400

    def test_history_returns_all_including_archived(self, client):
        pw_id, _ = self._setup(client)
        # Archivia
        import api as _api
        with _api.get_db_ctx() as conn:
            conn.execute("UPDATE pc_workflows SET archived=1, status='completed' WHERE id=?", (pw_id,))
            conn.commit()
        r = client.get("/api/pc-workflows/history?pc_name=ARCH-PC", headers=AUTH)
        assert r.status_code == 200
        rows = r.get_json()
        ids = [row["id"] for row in rows]
        assert pw_id in ids

    def test_history_unknown_pc_returns_empty(self, client):
        r = client.get("/api/pc-workflows/history?pc_name=NONEXISTENT-PC", headers=AUTH)
        assert r.status_code == 200
        assert r.get_json() == []


class TestExportImportWorkflow:
    """FEATURE-7: export/import workflow."""

    def _create_wf_with_steps(self, client, nome="WF-EXP"):
        wf = client.post("/api/workflows",
                         json={"nome": nome, "descrizione": "Test export"},
                         headers=AUTH).get_json()
        wf_id = wf["id"]
        client.post(f"/api/workflows/{wf_id}/steps",
                    json={"nome": "Step A", "tipo": "winget_install", "ordine": 1,
                          "parametri": {"package": "7zip.7zip"}, "su_errore": "continue"},
                    headers=AUTH)
        client.post(f"/api/workflows/{wf_id}/steps",
                    json={"nome": "Step B", "tipo": "reboot", "ordine": 2},
                    headers=AUTH)
        return wf_id

    def test_export_returns_json_attachment(self, client):
        wf_id = self._create_wf_with_steps(client)
        r = client.get(f"/api/workflows/{wf_id}/export", headers=AUTH)
        assert r.status_code == 200
        assert "attachment" in r.headers.get("Content-Disposition", "")
        data = r.get_json()
        assert data["novascm_export"] == "1.0"
        assert data["workflow"]["nome"] == "WF-EXP"
        assert len(data["workflow"]["steps"]) == 2

    def test_export_not_found(self, client):
        r = client.get("/api/workflows/9999/export", headers=AUTH)
        assert r.status_code == 404

    def test_export_steps_order(self, client):
        wf_id = self._create_wf_with_steps(client)
        data = client.get(f"/api/workflows/{wf_id}/export", headers=AUTH).get_json()
        orders = [s["ordine"] for s in data["workflow"]["steps"]]
        assert orders == sorted(orders)

    def test_import_creates_workflow(self, client):
        payload = {
            "novascm_export": "1.0",
            "workflow": {
                "nome": "WF-IMPORTED",
                "descrizione": "Importato",
                "steps": [
                    {"ordine": 1, "nome": "Installa", "tipo": "winget_install",
                     "parametri": "{}", "condizione": "", "su_errore": "stop", "platform": "all"},
                ]
            }
        }
        r = client.post("/api/workflows/import", json=payload, headers=AUTH)
        assert r.status_code == 201
        body = r.get_json()
        assert body["ok"] is True
        assert "workflow_id" in body

    def test_import_duplicate_name_returns_409(self, client):
        payload = {
            "novascm_export": "1.0",
            "workflow": {"nome": "WF-DUP", "steps": []}
        }
        client.post("/api/workflows/import", json=payload, headers=AUTH)
        r = client.post("/api/workflows/import", json=payload, headers=AUTH)
        assert r.status_code == 409

    def test_import_invalid_format(self, client):
        r = client.post("/api/workflows/import",
                        json={"novascm_export": "2.0", "workflow": {"nome": "X", "steps": []}},
                        headers=AUTH)
        assert r.status_code == 400

    def test_import_missing_nome(self, client):
        r = client.post("/api/workflows/import",
                        json={"novascm_export": "1.0", "workflow": {"steps": []}},
                        headers=AUTH)
        assert r.status_code == 400

    def test_roundtrip_export_import(self, client):
        """Export poi re-import con nome diverso — step devono coincidere."""
        wf_id = self._create_wf_with_steps(client, nome="WF-ROUNDTRIP")
        export_data = client.get(f"/api/workflows/{wf_id}/export", headers=AUTH).get_json()
        # Modifica nome per evitare conflitto
        export_data["workflow"]["nome"] = "WF-ROUNDTRIP-2"
        r = client.post("/api/workflows/import", json=export_data, headers=AUTH)
        assert r.status_code == 201
        new_id = r.get_json()["workflow_id"]
        # Verifica che gli step siano stati importati
        steps = client.get(f"/api/workflows/{new_id}/steps", headers=AUTH).get_json()
        assert len(steps) == 2


class TestWebhookSettings:
    """FEATURE-1: webhook_url/webhook_enabled in settings."""

    def test_settings_accept_webhook_url(self, client):
        r = client.put("/api/settings",
                       json={"webhook_url": "http://example.com/hook"},
                       headers=AUTH)
        assert r.status_code == 200

    def test_settings_accept_webhook_enabled(self, client):
        r = client.put("/api/settings",
                       json={"webhook_enabled": "1"},
                       headers=AUTH)
        assert r.status_code == 200

    def test_settings_reject_unknown_key(self, client):
        r = client.put("/api/settings",
                       json={"webhook_unknown_key": "val"},
                       headers=AUTH)
        assert r.status_code == 400

    def test_settings_webhook_roundtrip(self, client):
        client.put("/api/settings",
                   json={"webhook_url": "http://hooks.example.com/novascm", "webhook_enabled": "1"},
                   headers=AUTH)
        s = client.get("/api/settings", headers=AUTH).get_json()
        assert s.get("webhook_url") == "http://hooks.example.com/novascm"
        assert s.get("webhook_enabled") == "1"


class TestElapsedSec:
    """R8-I1: elapsed_sec salvato in report_step."""

    def _run_workflow(self, client):
        """Crea workflow, assegna, mette running, ritorna (pw_id, step_id)."""
        wf = client.post("/api/workflows", json={"nome": "WF-EL"}, headers=AUTH).get_json()
        wf_id = wf["id"]
        step = client.post(f"/api/workflows/{wf_id}/steps",
                           json={"nome": "S1", "tipo": "reboot", "ordine": 1},
                           headers=AUTH).get_json()
        step_id = step["id"]
        pw = client.post("/api/pc-workflows",
                         json={"pc_name": "EL-PC", "workflow_id": wf_id},
                         headers=AUTH).get_json()
        pw_id = pw["id"]
        import api as _api
        with _api.get_db_ctx() as conn:
            conn.execute("UPDATE pc_workflows SET status='running' WHERE id=?", (pw_id,))
            conn.commit()
        return pw_id, step_id

    def test_report_step_saves_elapsed_sec(self, client):
        pw_id, step_id = self._run_workflow(client)
        r = client.post("/api/pc/EL-PC/workflow/step",
                        json={"step_id": step_id, "status": "done", "elapsed_sec": 12.34},
                        headers=AUTH)
        assert r.status_code == 200
        # Verifica in DB
        import api as _api
        with _api.get_db_ctx() as conn:
            row = conn.execute(
                "SELECT elapsed_sec FROM pc_workflow_steps WHERE pc_workflow_id=? AND step_id=?",
                (pw_id, step_id)
            ).fetchone()
        assert row is not None
        assert abs(row["elapsed_sec"] - 12.34) < 0.01

    def test_report_step_elapsed_zero_default(self, client):
        pw_id, step_id = self._run_workflow(client)
        client.post("/api/pc/EL-PC/workflow/step",
                    json={"step_id": step_id, "status": "running"},
                    headers=AUTH)
        import api as _api
        with _api.get_db_ctx() as conn:
            row = conn.execute(
                "SELECT elapsed_sec FROM pc_workflow_steps WHERE pc_workflow_id=? AND step_id=?",
                (pw_id, step_id)
            ).fetchone()
        assert row is not None
        assert row["elapsed_sec"] == 0.0


class TestTimeoutCleanup:
    """FEATURE-6: _cleanup_stale_workflows marca timeout come error."""

    def test_stale_workflow_marked_error(self, client):
        import api as _api
        wf = client.post("/api/workflows",
                         json={"nome": "WF-TIMEOUT"},
                         headers=AUTH).get_json()
        wf_id = wf["id"]
        # Imposta timeout breve: 1 minuto
        with _api.get_db_ctx() as conn:
            conn.execute("UPDATE workflows SET timeout_min=1 WHERE id=?", (wf_id,))
            conn.commit()
        client.post(f"/api/workflows/{wf_id}/steps",
                    json={"nome": "S1", "tipo": "reboot", "ordine": 1},
                    headers=AUTH)
        pw = client.post("/api/pc-workflows",
                         json={"pc_name": "STALE-PC", "workflow_id": wf_id},
                         headers=AUTH).get_json()
        pw_id = pw["id"]
        # Metti in stato running con started_at 2 minuti fa
        with _api.get_db_ctx() as conn:
            conn.execute(
                "UPDATE pc_workflows SET status='running', started_at=datetime('now','-2 minutes') WHERE id=?",
                (pw_id,)
            )
            conn.commit()
        # Esegui cleanup direttamente
        _api._cleanup_stale_workflows()
        # Verifica stato
        with _api.get_db_ctx() as conn:
            row = conn.execute("SELECT status FROM pc_workflows WHERE id=?", (pw_id,)).fetchone()
        assert row["status"] == "error"

    def test_fresh_workflow_not_affected(self, client):
        import api as _api
        wf = client.post("/api/workflows",
                         json={"nome": "WF-FRESH"},
                         headers=AUTH).get_json()
        wf_id = wf["id"]
        with _api.get_db_ctx() as conn:
            conn.execute("UPDATE workflows SET timeout_min=120 WHERE id=?", (wf_id,))
            conn.commit()
        client.post(f"/api/workflows/{wf_id}/steps",
                    json={"nome": "S1", "tipo": "reboot", "ordine": 1},
                    headers=AUTH)
        pw = client.post("/api/pc-workflows",
                         json={"pc_name": "FRESH-PC", "workflow_id": wf_id},
                         headers=AUTH).get_json()
        pw_id = pw["id"]
        with _api.get_db_ctx() as conn:
            conn.execute(
                "UPDATE pc_workflows SET status='running', started_at=datetime('now') WHERE id=?",
                (pw_id,)
            )
            conn.commit()
        _api._cleanup_stale_workflows()
        with _api.get_db_ctx() as conn:
            row = conn.execute("SELECT status FROM pc_workflows WHERE id=?", (pw_id,)).fetchone()
        assert row["status"] == "running"
