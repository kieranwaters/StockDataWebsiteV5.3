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
            let labels = JSON.parse(element.getAttribute('data-labels')).reverse(); // Invert labels
            let values = JSON.parse(element.getAttribute('data-values')).reverse(); // Invert values
            let scalingLabel = element.getAttribute('data-scaling-label') || '';
            let metricName = element.getAttribute('data-metric') || '';

            // Clean up `(null)` and "null" strings from metricName and scalingLabel
            function cleanString(str) {
                if (!str) return '';
                str = str.trim();
                // Replace literal "(null)" with empty and if string is "null"
                if (str.toLowerCase() === 'null') str = '';
                str = str.replace(/\(null\)/gi, '').trim();
                return str;
            }

            scalingLabel = cleanString(scalingLabel);
            metricName = cleanString(metricName);

            // Function to clean and parse values into floats
            function parseNumeric(val) {
                if (typeof val !== 'string') return val;
                // Remove all commas and extra spaces
                val = val.replace(/,/g, '').trim();
                let num = parseFloat(val);
                return isNaN(num) ? val : num;
            }

            // Convert values to numeric if possible
            let numericValues = values.map(v => parseNumeric(v));

            // If no scaling label provided, determine scaling factor
            if (!scalingLabel) {
                let validNumbers = numericValues.filter(v => typeof v === 'number');
                let scalingFactor = 0;
                if (validNumbers.length > 0) {
                    const maxVal = Math.max(...validNumbers);
                    if (maxVal >= 1_000_000_000) {
                        scalingFactor = 9;
                        scalingLabel = "in Billions $";
                    } else if (maxVal >= 1_000_000) {
                        scalingFactor = 6;
                        scalingLabel = "in Millions $";
                    } else if (maxVal >= 1_000) {
                        scalingFactor = 3;
                        scalingLabel = "in Thousands $";
                    } else {
                        scalingFactor = 0;
                        scalingLabel = ""; // no scaling needed
                    }
                }

                // Apply scaling if needed
                if (scalingFactor > 0) {
                    numericValues = numericValues.map(val => {
                        if (typeof val === 'number') {
                            let scaledVal = val / Math.pow(10, scalingFactor);
                            return scaledVal;
                        }
                        return val;
                    });
                }
            }

            // Convert numeric values back to Chart.js consumable data
            let finalValues = numericValues.map(v => (typeof v === 'number' ? v : null));

            // Construct the chart titles
            const chartTitle = scalingLabel ? `${metricName} (${scalingLabel})` : metricName;
            const yAxisTitle = scalingLabel ? `Amount (${scalingLabel})` : "Amount";

            // Prepare the chart data (now in chronological order)
            const chartData = {
                labels: labels,
                datasets: [{
                    label: metricName,
                    data: finalValues,
                    borderColor: 'rgba(75, 192, 192, 1)',
                    backgroundColor: 'rgba(75, 192, 192, 0.2)',
                    fill: true,
                    tension: 0.1
                }]
            };

            // Prepare the chart options
            const chartOptions = {
                responsive: true,
                plugins: {
                    title: {
                        display: true,
                        text: chartTitle || 'Historical Data'
                    },
                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                let label = context.dataset.label || '';
                                if (label) {
                                    label += ': ';
                                }
                                if (context.parsed.y !== null) {
                                    let valStr = context.parsed.y.toLocaleString();
                                    if (scalingLabel) {
                                        valStr += ` ${scalingLabel}`;
                                    }
                                    label += valStr;
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
                            text: yAxisTitle
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

            // Update the modal title
            const modalTitle = document.querySelector('#graphModal .modal-title');
            if (scalingLabel) {
                modalTitle.textContent = `Historical Data (${scalingLabel})`;
            } else {
                modalTitle.textContent = 'Historical Data';
            }

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
