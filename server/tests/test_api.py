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
                   data=json.dumps({"novascm_url": "http://example.com"}),
                   content_type="application/json")
        data = client.get("/api/settings", headers=AUTH).get_json()
        assert data.get("novascm_url") == "http://example.com"

    def test_put_settings_returns_all_settings(self, client):
        r = client.put("/api/settings", headers=AUTH,
                       data=json.dumps({"key1": "v1", "key2": "v2"}),
                       content_type="application/json")
        assert r.status_code == 200
        data = r.get_json()
        assert data["key1"] == "v1"
        assert data["key2"] == "v2"

    def test_put_settings_upserts_existing_key(self, client):
        client.put("/api/settings", headers=AUTH,
                   data=json.dumps({"mykey": "first"}),
                   content_type="application/json")
        client.put("/api/settings", headers=AUTH,
                   data=json.dumps({"mykey": "second"}),
                   content_type="application/json")
        data = client.get("/api/settings", headers=AUTH).get_json()
        assert data["mykey"] == "second"

    def test_put_settings_none_value_stored_as_empty_string(self, client):
        client.put("/api/settings", headers=AUTH,
                   data=json.dumps({"nullkey": None}),
                   content_type="application/json")
        data = client.get("/api/settings", headers=AUTH).get_json()
        assert data.get("nullkey") == ""

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
        steps = r.get_json()
        assert any(s["step_name"] == "winget_install" for s in steps)

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
