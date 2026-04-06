document.addEventListener('DOMContentLoaded', () => {
    fetchStats();
    fetchTickets();

    // Auto-refresh every 30 seconds
    setInterval(() => {
        fetchStats();
        fetchTickets();
    }, 30000);

    const refreshBtn = document.getElementById('refresh-btn');
    refreshBtn.addEventListener('click', () => {
        refreshBtn.classList.add('fa-spin');
        Promise.all([fetchStats(), fetchTickets()]).finally(() => {
            setTimeout(() => refreshBtn.classList.remove('fa-spin'), 600);
        });
    });
});

async function fetchStats() {
    try {
        const response = await fetch('/api/stats');
        const stats = await response.json();
        
        document.getElementById('total-tickets').textContent = stats.total;
        document.getElementById('processed-count').textContent = stats.processed;
        document.getElementById('failed-count').textContent = stats.failed;
    } catch (error) {
        console.error('Error fetching stats:', error);
    }
}

async function fetchTickets() {
    try {
        const response = await fetch('/api/tickets');
        const tickets = await response.json();
        
        const tbody = document.getElementById('tickets-body');
        tbody.innerHTML = '';

        if (tickets.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" style="text-align:center; padding: 3rem; color: #a0a0a0;">No history found yet. Processing first emails...</td></tr>';
            return;
        }

        tickets.forEach(ticket => {
            const row = document.createElement('tr');
            
            const date = new Date(ticket.receivedAt).toLocaleString();
            const statusClass = getStatusClass(ticket.currentPipelineStatus);
            const adoId = ticket.adoWorkItemId ? `#${ticket.adoWorkItemId}` : 'Pending';
            const adoUrl = ticket.adoUrl ? `<a href="${ticket.adoUrl}" target="_blank" class="ado-link">${adoId} <i class="fa-solid fa-external-link"></i></a>` : `<span style="color:#a0a0a0">${adoId}</span>`;
            const adoState = ticket.adoItemState || 'N/A';
            const stateClass = getAdoStateClass(adoState);

            row.innerHTML = `
                <td style="color: #a0a0a0; font-size: 0.8rem;">${date}</td>
                <td style="font-weight: 600; max-width: 300px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;" title="${ticket.subject}">${ticket.subject}</td>
                <td>${ticket.senderEmail}</td>
                <td><span class="badge" style="background: rgba(124, 77, 255, 0.1); color: #7c4dff;">${ticket.extractedDepartment || 'Draft'}</span></td>
                <td>${adoUrl}</td>
                <td><span class="badge ${stateClass}">${adoState}</span></td>
                <td><span class="badge ${statusClass}">${ticket.currentPipelineStatus}</span></td>
            `;
            
            tbody.appendChild(row);
        });
    } catch (error) {
        console.error('Error fetching tickets:', error);
    }
}

function getStatusClass(status) {
    switch (status) {
        case 'AdoCreated':
        case 'Completed':
            return 'badge-success';
        case 'EmailReceived':
        case 'LlmProcessing':
        case 'AdoCreating':
            return 'badge-warning';
        case 'AdoFailed':
        case 'LlmFailed':
            return 'badge-error';
        default:
            return 'badge-warning';
    }
}

function getAdoStateClass(state) {
    switch (state.toLowerCase()) {
        case 'done':
        case 'closed':
            return 'badge-success';
        case 'doing':
        case 'active':
        case 'in progress':
            return 'badge-warning';
        case 'to do':
        case 'new':
            return 'badge-accent';
        default:
            return 'badge-accent';
    }
}
