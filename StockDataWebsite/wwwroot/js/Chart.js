// wwwroot/js/chart.js

// wwwroot/js/chart.js

// wwwroot/js/chart.js

document.addEventListener('DOMContentLoaded', function () {
    const graphModal = new bootstrap.Modal(document.getElementById('graphModal'));
    const metricChartCtx = document.getElementById('metricChart').getContext('2d');
    let metricChart; // To hold the Chart instance

    document.querySelectorAll('.element-name').forEach(function (element) {
        element.addEventListener('click', function () {
            // Show the loading indicator
            document.getElementById('loadingIndicator').style.display = 'block';
            document.getElementById('metricChart').style.display = 'none';

            // Retrieve data attributes
            const labels = JSON.parse(element.getAttribute('data-labels'));
            const values = JSON.parse(element.getAttribute('data-values'));
            const scalingLabel = element.getAttribute('data-scaling-label');

            // Prepare the chart data (no scaling applied)
            const chartData = {
                labels: labels,
                datasets: [{
                    label: element.getAttribute('data-metric'),
                    data: values, // Use the already scaled values
                    borderColor: 'rgba(75, 192, 192, 1)',
                    backgroundColor: 'rgba(75, 192, 192, 0.2)',
                    fill: true,
                    tension: 0.1
                }]
            };

            // Prepare the chart options, including the Y-axis scaling label
            const chartOptions = {
                responsive: true,
                plugins: {
                    title: {
                        display: true,
                        text: `${element.getAttribute('data-metric')} (${scalingLabel})`
                    },
                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                let label = context.dataset.label || '';
                                if (label) {
                                    label += ': ';
                                }
                                if (context.parsed.y !== null) {
                                    label += context.parsed.y;
                                    // Append scaling label
                                    label += ` ${scalingLabel}`;
                                }
                                return label;
                            }
                        }
                    }
                },
                scales: {
                    y: {
                        beginAtZero: true,
                        title: {
                            display: true,
                            text: `Amount (${scalingLabel})` // Incorporate the scaling label here
                        }
                    },
                    x: {
                        title: {
                            display: true,
                            text: 'Period'
                        }
                    }
                }
            };

            // Update the modal title with the scaling label (optional)
            const modalTitle = document.querySelector('#graphModal .modal-title');
            modalTitle.textContent = `Historical Data (${scalingLabel})`;

            // If a chart already exists, destroy it before creating a new one
            if (metricChart) {
                metricChart.destroy();
            }

            // Create the chart
            metricChart = new Chart(metricChartCtx, {
                type: 'line',
                data: chartData,
                options: chartOptions
            });

            // Hide the loading indicator and show the chart
            document.getElementById('loadingIndicator').style.display = 'none';
            document.getElementById('metricChart').style.display = 'block';

            // Show the modal
            graphModal.show();
        });
    });
});
