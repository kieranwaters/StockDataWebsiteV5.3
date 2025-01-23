document.addEventListener('DOMContentLoaded', function () {
    const graphModal = new bootstrap.Modal(document.getElementById('graphModal')); //Bootstrap modal
    const metricChartCtx = document.getElementById('metricChart').getContext('2d'); //Chart context
    let metricChart; //Chart instance

    document.querySelectorAll('.element-name').forEach(function (element) {
        element.addEventListener('click', function () {
            document.getElementById('loadingIndicator').style.display = 'block'; //Show loading
            document.getElementById('metricChart').style.display = 'none';

            let labels = JSON.parse(element.getAttribute('data-labels')).reverse(); //Reversed so oldest->latest
            let values = JSON.parse(element.getAttribute('data-values')).reverse(); //Reversed likewise
            let scalingLabel = element.getAttribute('data-scaling-label') || ''; //If passed from server
            let metricName = element.getAttribute('data-metric') || '';

            function cleanString(str) { //Inline function to remove null markers
                if (!str) return '';
                str = str.trim();
                if (str.toLowerCase() === 'null') str = '';
                str = str.replace(/\(null\)/gi, '').trim();
                return str;
            }
            scalingLabel = cleanString(scalingLabel);
            metricName = cleanString(metricName);

            function parseNumeric(val) { //Inline parser to remove commas and parse float
                if (typeof val !== 'string') return val;
                val = val.replace(/,/g, '').trim();
                let num = parseFloat(val);
                return isNaN(num) ? val : num;
            }

            let numericValues = values.map(v => parseNumeric(v)); //Convert strings->numbers when possible

            if (!scalingLabel) { //If no scaling info from server, try to auto-calc
                let validNumbers = numericValues.filter(v => typeof v === 'number');
                let scalingFactor = 0;
                if (validNumbers.length > 0) {
                    const maxVal = Math.max(...validNumbers);
                    if (maxVal >= 1_000_000_000) { scalingFactor = 9; scalingLabel = "in Billions $"; }
                    else if (maxVal >= 1_000_000) { scalingFactor = 6; scalingLabel = "in Millions $"; }
                    else if (maxVal >= 1000) { scalingFactor = 3; scalingLabel = "in Thousands $"; }
                }
                if (scalingFactor > 0) { //Apply the factor
                    numericValues = numericValues.map(val => {
                        if (typeof val === 'number') { return val / Math.pow(10, scalingFactor); }
                        return val;
                    });
                }
            }

            let finalValues = numericValues.map(v => (typeof v === 'number' ? v : null)); //Chart needs numeric or null
            const chartTitle = scalingLabel ? `${metricName} (${scalingLabel})` : metricName;
            const yAxisTitle = scalingLabel ? `Amount (${scalingLabel})` : "Amount";

            const chartData = {
                labels: labels,
                datasets: [{
                    label: metricName,
                    data: finalValues,
                    borderColor: 'rgba(75,192,192,1)',
                    backgroundColor: 'rgba(75,192,192,0.2)',
                    fill: true,
                    tension: 0.1
                }]
            };

            const chartOptions = {
                responsive: true, //Make chart resize to fit
                maintainAspectRatio: false, //Important for mobile responsiveness
                plugins: {
                    title: { display: true, text: chartTitle || 'Historical Data' },
                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                let label = context.dataset.label || '';
                                if (label) { label += ': '; }
                                if (context.parsed.y !== null) {
                                    let valStr = context.parsed.y.toLocaleString();
                                    if (scalingLabel) { valStr += ` ${scalingLabel}`; }
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
                        title: { display: true, text: yAxisTitle },
                        ticks: { callback: function (value) { return value.toLocaleString(); } }, //Thousands separator
                        grid: { color: 'rgba(200,200,200,0.2)' }
                    },
                    x: {
                        title: { display: true, text: 'Period' },
                        grid: { color: 'rgba(200,200,200,0.2)' }
                    }
                }
            };

            const modalTitle = document.querySelector('#graphModal .modal-title');
            modalTitle.textContent = scalingLabel ? `Historical Data (${scalingLabel})` : 'Historical Data';

            if (metricChart) { metricChart.destroy(); } //Destroy existing chart if any
            metricChart = new Chart(metricChartCtx, { type: 'line', data: chartData, options: chartOptions });

            document.getElementById('loadingIndicator').style.display = 'none'; //Hide loading
            document.getElementById('metricChart').style.display = 'block';
            graphModal.show(); //Open the modal
        });
    });
});
