(function () {
    "use strict";
    if (window.formDevice) return;
    let database;
    function open() {
        if (!window.indexedDB) return Promise.reject(new Error("Persistent browser storage is unavailable. Enable site storage to save your journal."));
        if (database) return Promise.resolve(database);
        return new Promise((resolve, reject) => {
            const request = indexedDB.open("form-habittracker", 1);
            request.onupgradeneeded = () => {
                if (!request.result.objectStoreNames.contains("documents")) request.result.createObjectStore("documents");
            };
            request.onerror = () => reject(request.error || new Error("Could not open browser storage."));
            request.onblocked = () => reject(new Error("Journal storage is busy. Close older app tabs and refresh."));
            request.onsuccess = () => {
                database = request.result;
                database.onversionchange = () => { database.close(); database = undefined; };
                resolve(database);
            };
        });
    }
    function fail(error, callback) {
        callback(error && error.name === "QuotaExceededError"
            ? "Device storage is full. Export your journal and free some space before saving."
            : (error && error.message) || "The device journal could not be saved.");
    }
    window.formDevice = {
        read(key, success, failure) {
            open().then(db => new Promise((resolve, reject) => {
                const transaction = db.transaction("documents", "readonly");
                const request = transaction.objectStore("documents").get(key);
                request.onsuccess = () => resolve(JSON.stringify(request.result || null));
                request.onerror = () => reject(request.error);
                transaction.onabort = () => reject(transaction.error);
            })).then(success, error => fail(error, failure));
        },
        write(key, expected, json, success, failure) {
            open().then(db => new Promise((resolve, reject) => {
                if (!Number.isSafeInteger(expected) || expected < 0) { reject(new Error("Invalid journal version.")); return; }
                let written = false;
                const transaction = db.transaction("documents", "readwrite");
                const store = transaction.objectStore("documents");
                const read = store.get(key);
                read.onsuccess = () => {
                    const previous = read.result;
                    if ((previous ? previous.version : 0) !== expected) return;
                    store.put({ version: expected + 1, json }, key);
                    written = true;
                };
                read.onerror = () => reject(read.error);
                transaction.oncomplete = () => resolve(written ? "true" : "false");
                transaction.onerror = () => reject(transaction.error);
                transaction.onabort = () => reject(transaction.error || new Error("Journal transaction was interrupted."));
            })).then(success, error => fail(error, failure));
        },
        exportCsv(fileName, csv) {
            const url = URL.createObjectURL(new Blob(["\uFEFF", csv], { type: "text/csv;charset=utf-8" }));
            const anchor = document.createElement("a");
            anchor.href = url; anchor.download = fileName; anchor.style.display = "none";
            document.body.appendChild(anchor); anchor.click(); anchor.remove();
            setTimeout(() => URL.revokeObjectURL(url), 10000);
        },
        launchDemo() { return new URLSearchParams(location.search).get("demo") === "1"; },
        initialView() { return new URLSearchParams(location.search).get("view") || "Home"; },
        lifecycle(callback) {
            document.addEventListener("visibilitychange", () => callback(document.hidden ? "suspend" : "resume"));
            window.addEventListener("pageshow", () => callback("resume"));
            window.addEventListener("pagehide", () => callback("suspend"));
        },
        inputMode(root, mode) {
            if (!root) return;
            const input = root.matches && root.matches("input,textarea") ? root : root.querySelector("input,textarea");
            if (input) input.setAttribute("inputmode", mode);
        },
        annotate(root, role, label, enabled, state) {
            if (!root) return false;
            if (role === "textbox") {
                const input = root.matches("input,textarea") ? root : root.querySelector("input,textarea");
                if (input && label) input.setAttribute("aria-label", label);
                return !!input;
            }
            root.setAttribute("role", role);
            if (label) root.setAttribute("aria-label", label);
            root.setAttribute("aria-disabled", enabled ? "false" : "true");
            if (role === "checkbox") root.setAttribute("aria-checked", state);
            if (role === "combobox") { root.setAttribute("aria-expanded", state); root.setAttribute("aria-haspopup", "listbox"); }
            if (role === "option") root.setAttribute("aria-selected", state);
            return true;
        },
        ready() {
            const boot = document.getElementById("boot-status");
            if (boot) boot.remove();
        }
    };
    function fitViewport() {
        const height = window.visualViewport ? window.visualViewport.height : window.innerHeight;
        document.documentElement.style.setProperty("--form-height", height + "px");
    }
    fitViewport();
    if (window.visualViewport) window.visualViewport.addEventListener("resize", fitViewport);
    window.addEventListener("resize", fitViewport);
}());
