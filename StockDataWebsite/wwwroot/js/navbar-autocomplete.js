// wwwroot/js/navbar-autocomplete.js

document.addEventListener('DOMContentLoaded', function () {
    const navbarInput = document.getElementById('NavbarCompanySearch');
    const navbarSuggestions = document.getElementById('navbarSuggestions');
    const currentCompanyName = window.currentCompanyName; // Use a global variable

    if (navbarInput) {
        navbarInput.addEventListener('input', function () {
            const query = this.value.trim();
            if (query.length >= 2) {
                fetch(`/api/companies/search?query=${encodeURIComponent(query)}`)
                    .then(response => response.json())
                    .then(data => {
                        navbarSuggestions.innerHTML = '';
                        if (data.length > 0) {
                            data.forEach(company => {
                                // Exclude the current company if on StockData page
                                if (currentCompanyName && company.companyName.toLowerCase() === currentCompanyName.toLowerCase()) {
                                    return; // Skip adding this company to suggestions
                                }

                                const suggestionItem = document.createElement('a');
                                suggestionItem.classList.add('list-group-item', 'list-group-item-action');
                                suggestionItem.setAttribute('role', 'option');
                                suggestionItem.textContent = `${company.companyName} (${company.companySymbol})`;

                                // When a suggestion is clicked
                                suggestionItem.addEventListener('click', function () {
                                    navbarInput.value = company.companyName;
                                    navbarSuggestions.innerHTML = '';
                                });

                                navbarSuggestions.appendChild(suggestionItem);
                            });
                        } else {
                            navbarSuggestions.innerHTML = '<div class="list-group-item">No results found</div>';
                        }
                    })
                    .catch(error => {
                        console.error('Error fetching suggestions:', error);
                        navbarSuggestions.innerHTML = '<div class="list-group-item">Error fetching suggestions</div>';
                    });
            } else {
                navbarSuggestions.innerHTML = '';
            }
        });

        // Hide suggestions when clicking outside the search input or suggestions dropdown
        document.addEventListener('click', function (e) {
            if (!navbarInput.contains(e.target) && !navbarSuggestions.contains(e.target)) {
                navbarSuggestions.innerHTML = '';
            }
        });
    }
});
