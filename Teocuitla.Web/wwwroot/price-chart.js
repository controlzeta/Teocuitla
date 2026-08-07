const teocuitlaChartInstances = {};

function destroyChartIfExists(canvasId) {
    if (teocuitlaChartInstances[canvasId]) {
        teocuitlaChartInstances[canvasId].destroy();
        delete teocuitlaChartInstances[canvasId];
    }
}

window.teocuitlaRenderPriceChart = (canvasId, labels, series) => {
    const ctx = document.getElementById(canvasId);
    if (!ctx) {
        setTimeout(() => {
            const retryCtx = document.getElementById(canvasId);
            if (retryCtx) {
                window.teocuitlaRenderPriceChart(canvasId, labels, series);
            }
        }, 100);
        return;
    }

    destroyChartIfExists(canvasId);

    const colors = [
        '#d4af37', // Champagne Gold
        '#10b981', // Emerald
        '#94a3b8', // Slate Titanium
        '#f59e0b', // Amber
        '#38bdf8', // Sky Blue
        '#e11d48'  // Rose Crimson
    ];


    const datasets = series.map((s, idx) => {
        const color = colors[idx % colors.length];
        return {
            label: s.name,
            data: s.data,
            borderColor: color,
            backgroundColor: (context) => {
                const chart = context.chart;
                const {ctx, chartArea} = chart;
                if (!chartArea) return null;
                const gradient = ctx.createLinearGradient(0, chartArea.top, 0, chartArea.bottom);
                gradient.addColorStop(0, color + '33'); // 20% opacity
                gradient.addColorStop(1, color + '00'); // 0% opacity
                return gradient;
            },
            fill: true,
            tension: 0.35,
            pointBackgroundColor: color,
            pointBorderColor: '#0d0d11',
            pointBorderWidth: 2,
            pointRadius: 4,
            pointHoverRadius: 7,
            pointHoverBackgroundColor: color,
            pointHoverBorderColor: '#ffffff',
            pointHoverBorderWidth: 2,
            borderWidth: 3
        };
    });

    teocuitlaChartInstances[canvasId] = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: datasets
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: {
                intersect: false,
                mode: 'index'
            },
            plugins: {
                legend: {
                    position: 'top',
                    labels: {
                        color: '#a1a1aa',
                        font: {
                            family: "'Outfit', sans-serif",
                            size: 13,
                            weight: '500'
                        },
                        boxWidth: 12,
                        boxHeight: 12,
                        padding: 15,
                        usePointStyle: true,
                        pointStyle: 'circle'
                    }
                },
                tooltip: {
                    backgroundColor: '#14141a',
                    titleColor: '#ffffff',
                    bodyColor: '#a1a1aa',
                    borderColor: 'rgba(255, 255, 255, 0.08)',
                    borderWidth: 1,
                    padding: 12,
                    cornerRadius: 8,
                    titleFont: {
                        family: "'Outfit', sans-serif",
                        size: 13,
                        weight: '600'
                    },
                    bodyFont: {
                        family: "'Inter', sans-serif",
                        size: 12
                    },
                    callbacks: {
                        label: function(context) {
                            let label = context.dataset.label || '';
                            if (label) {
                                label += ': ';
                            }
                            if (context.parsed.y !== null) {
                                label += new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' }).format(context.parsed.y);
                            }
                            return label;
                        }
                    }
                }
            },
            scales: {
                x: {
                    grid: {
                        color: 'rgba(255, 255, 255, 0.03)',
                        borderColor: 'rgba(255, 255, 255, 0.05)'
                    },
                    ticks: {
                        color: '#71717a',
                        font: {
                            family: "'Inter', sans-serif",
                            size: 11
                        }
                    }
                },
                y: {
                    grid: {
                        color: 'rgba(255, 255, 255, 0.05)',
                        borderColor: 'rgba(255, 255, 255, 0.05)'
                    },
                    ticks: {
                        color: '#71717a',
                        font: {
                            family: "'Inter', sans-serif",
                            size: 11
                        },
                        callback: function(value) {
                            return new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN', maximumFractionDigits: 0 }).format(value);
                        }
                    }
                }
            }
        }
    });
};

window.teocuitlaRenderDonutChart = (canvasId, labels, data, colors) => {
    const ctx = document.getElementById(canvasId);
    if (!ctx) {
        setTimeout(() => window.teocuitlaRenderDonutChart(canvasId, labels, data, colors), 100);
        return;
    }

    destroyChartIfExists(canvasId);

    const defaultColors = ['#8f44fd', '#ef4444', '#f59e0b', '#06b6d4', '#10b981', '#64748b'];
    const bgColors = colors && colors.length ? colors : defaultColors;

    teocuitlaChartInstances[canvasId] = new Chart(ctx, {
        type: 'doughnut',
        data: {
            labels: labels,
            datasets: [{
                data: data,
                backgroundColor: bgColors,
                borderColor: '#0d0d11',
                borderWidth: 3,
                hoverOffset: 6
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            cutout: '68%',
            plugins: {
                legend: {
                    position: 'right',
                    labels: {
                        color: '#a1a1aa',
                        font: { family: "'Outfit', sans-serif", size: 12 },
                        boxWidth: 10,
                        usePointStyle: true
                    }
                },
                tooltip: {
                    backgroundColor: '#14141a',
                    titleColor: '#ffffff',
                    bodyColor: '#a1a1aa',
                    borderColor: 'rgba(255, 255, 255, 0.08)',
                    borderWidth: 1,
                    padding: 10,
                    cornerRadius: 8
                }
            }
        }
    });
};

window.teocuitlaRenderBarChart = (canvasId, labels, series, isStacked = false) => {
    const ctx = document.getElementById(canvasId);
    if (!ctx) {
        setTimeout(() => window.teocuitlaRenderBarChart(canvasId, labels, series, isStacked), 100);
        return;
    }

    destroyChartIfExists(canvasId);

    const defaultColors = ['#8f44fd', '#10b981', '#ef4444', '#f59e0b', '#06b6d4'];

    const datasets = series.map((s, idx) => ({
        label: s.name,
        data: s.data,
        backgroundColor: s.color || defaultColors[idx % defaultColors.length],
        borderRadius: 4
    }));

    teocuitlaChartInstances[canvasId] = new Chart(ctx, {
        type: 'bar',
        data: { labels: labels, datasets: datasets },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'top',
                    labels: { color: '#a1a1aa', font: { family: "'Outfit', sans-serif", size: 12 }, usePointStyle: true }
                },
                tooltip: {
                    backgroundColor: '#14141a',
                    titleColor: '#ffffff',
                    bodyColor: '#a1a1aa',
                    borderColor: 'rgba(255, 255, 255, 0.08)',
                    borderWidth: 1,
                    padding: 10,
                    cornerRadius: 8
                }
            },
            scales: {
                x: {
                    stacked: isStacked,
                    grid: { color: 'rgba(255, 255, 255, 0.03)' },
                    ticks: { color: '#71717a', font: { family: "'Inter', sans-serif", size: 11 } }
                },
                y: {
                    stacked: isStacked,
                    grid: { color: 'rgba(255, 255, 255, 0.05)' },
                    ticks: { color: '#71717a', font: { family: "'Inter', sans-serif", size: 11 } }
                }
            }
        }
    });
};

