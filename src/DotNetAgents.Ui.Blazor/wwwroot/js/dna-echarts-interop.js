/* global echarts */
(function () {
    const charts = {};
    const registeredMaps = new Set();

    async function ensureMapRegistered(mapName, geoJsonUrl) {
        if (registeredMaps.has(mapName)) {
            return;
        }
        const response = await fetch(geoJsonUrl);
        if (!response.ok) {
            throw new Error('dnaEchartsInterop: failed to load map ' + geoJsonUrl);
        }
        const geoJson = await response.json();
        echarts.registerMap(mapName, geoJson);
        registeredMaps.add(mapName);
    }

    window.dnaEchartsInterop = {
        /**
         * @param {string} containerId - element id
         * @param {string} optionJson - ECharts option JSON
         * @param {string | null} mapRegistrationJson - optional { name, geoJsonUrl }
         */
        render: async function (containerId, optionJson, mapRegistrationJson) {
            const el = document.getElementById(containerId);
            if (!el) {
                return;
            }
            if (mapRegistrationJson) {
                const mr = JSON.parse(mapRegistrationJson);
                await ensureMapRegistered(mr.name, mr.geoJsonUrl);
            }
            const option = JSON.parse(optionJson);
            if (charts[containerId]) {
                charts[containerId].dispose();
                delete charts[containerId];
            }
            const chart = echarts.init(el);
            chart.setOption(option);
            charts[containerId] = chart;
        },

        resize: function (containerId) {
            const c = charts[containerId];
            if (c) {
                c.resize();
            }
        },

        dispose: function (containerId) {
            const c = charts[containerId];
            if (c) {
                c.dispose();
                delete charts[containerId];
            }
        }
    };
})();
