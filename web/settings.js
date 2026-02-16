require(['pluginapi'], function (api) {
    api.getPluginConfiguration().then(function (config) {
        // Set initial value
        document.getElementById("apiKey").value = config.ApiKey || "";
        // (We’ll do mappings in the next step)
    });
});