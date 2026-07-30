let teocuitlaChartInstance = null;

window.teocuitlaRenderPriceChart = (canvasId, labels, series) => {
    const ctx = document.getElementById(canvasId);
    if (!ctx) {
        // If element is not found, retry once after a short delay (for Blazor DOM updates)
        setTimeout(() => {
            const retryCtx = document.getElementById(canvasId);
            if (retryCtx) {
                window.teocuitlaRenderPriceChart(canvasId, labels, series);
            }
        }, 100);
        return;
    }

    if (teocuitlaChartInstance) {
        teocuitlaChartInstance.destroy();
    }

    const colors = [
        '#8f44fd', // Purple
        '#10b981', // Green
        '#06b6d4', // Cyan
        '#f59e0b', // Amber
        '#3b82f6', // Blue
        '#ef4444'  // Red
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

    teocuitlaChartInstance = new Chart(ctx, {
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
