define(['ApiClient', 'pluginManager'], function (ApiClient, pluginManager) {
    'use strict';

    var pluginId = '9C425866-D572-4740-9119-2041D99279D1'; // Ensure this matches your plugin GUID

    function getPluginConfiguration() {
        return ApiClient.getPluginConfiguration(pluginId);
    }

    function updatePluginConfiguration(config) {
        return ApiClient.updatePluginConfiguration(pluginId, config);
    }

    function loadConfig(page) {
        getPluginConfiguration().then(function (config) {
            page.querySelector('#txtApiKey').value = config.ApiKey || "3";

            var mappingsDiv = page.querySelector('#mappingsList');
            mappingsDiv.innerHTML = '';

            if (config.LeagueMappings && config.LeagueMappings.length > 0) {
                config.LeagueMappings.forEach(function (mapping) {
                    addMappingRow(page, mapping.Name, mapping.LeagueId);
                });
            }
        });
    }

    function addMappingRow(page, nameVal, idVal) {
        var mappingsDiv = page.querySelector('#mappingsList');
        var row = document.createElement('div');
        row.style.marginBottom = '10px';
        row.className = "mapping-row";

        var nameInput = document.createElement('input');
        nameInput.type = 'text';
        nameInput.placeholder = 'Folder Name (e.g. NHL)';
        nameInput.className = 'mapping-name';
        nameInput.value = nameVal || '';
        nameInput.style.marginRight = '10px';

        var idInput = document.createElement('input');
        idInput.type = 'text';
        idInput.placeholder = 'League ID (e.g. 4380)';
        idInput.className = 'mapping-id';
        idInput.value = idVal || '';
        idInput.style.marginRight = '10px';

        var removeBtn = document.createElement('button');
        removeBtn.type = 'button';
        removeBtn.textContent = 'Remove';
        removeBtn.className = 'raised';
        removeBtn.onclick = function () {
            mappingsDiv.removeChild(row);
        };

        row.appendChild(nameInput);
        row.appendChild(idInput);
        row.appendChild(removeBtn);
        mappingsDiv.appendChild(row);
    }

    function saveConfig(page) {
        var apiKey = page.querySelector('#txtApiKey').value;

        var mappings = [];
        var rows = page.querySelectorAll('.mapping-row');
        rows.forEach(function (row) {
            var name = row.querySelector('.mapping-name').value;
            var id = row.querySelector('.mapping-id').value;
            if (name && id) {
                mappings.push({ Name: name, LeagueId: id });
            }
        });

        // Get current config first to preserve other fields if any
        getPluginConfiguration().then(function (config) {
            config.ApiKey = apiKey;
            config.LeagueMappings = mappings;

            updatePluginConfiguration(config).then(function (result) {
                Dashboard.alert({
                    message: "Settings saved successfully!",
                    title: "Success"
                });
            });
        });
    }

    return function (view, params) {
        view.querySelector('#btnAddMapping').addEventListener('click', function () {
            addMappingRow(view);
        });

        view.querySelector('#btnSave').addEventListener('click', function () {
            saveConfig(view);
        });

        view.addEventListener('viewshow', function () {
            loadConfig(view);
        });
    };
});