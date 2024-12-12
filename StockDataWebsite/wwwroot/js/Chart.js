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
            const labels = JSON.parse(element.getAttribute('data-labels')).reverse(); // Invert labels
            const values = JSON.parse(element.getAttribute('data-values')).reverse(); // Invert values
            const scalingLabel = element.getAttribute('data-scaling-label');

            // Prepare the chart data (now in chronological order)
            const chartData = {
                labels: labels, // Oldest to newest
                datasets: [{
                    label: element.getAttribute('data-metric'),
                    data: values, // Corresponding values
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
                            text: `Amount (${scalingLabel})`
                        },
                        ticks: {
                            callback: function (value) {
                                return value.toLocaleString(); // Adds thousand separators
                            }
                        },
                        grid: {
                            color: 'rgba(200, 200, 200, 0.2)'
                        }
                    },
                    x: {
                        title: {
                            display: true,
                            text: 'Period'
                        },
                        grid: {
                            color: 'rgba(200, 200, 200, 0.2)'
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
