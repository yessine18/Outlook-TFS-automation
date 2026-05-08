// ========================================
// DASHBOARD CONFIG
// ========================================

const CONFIG = {
    apiBaseUrl: '/api',
    refreshInterval: 3000, // 3 seconds
    animationDuration: 300,
    countersEnabled: true,
};

// ========================================
// MOCK DATA (Replace with API calls)
// ========================================

const MOCK_STATS = {
    pipeline: {
        inbox: 145,
        llm: 42,
        routing: 28,
        rag: 18,
        devops: 12,
        completed: 1247,
    },
    kpis: {
        totalProcessed: 1247,
        inQueue: 8,
        successRate: 94.2,
        avgProcessingTime: 2.3,
        aiAutoResolved: 342,
        failedTickets: 72,
        systemUptime: 99.9,
        apiResponseTime: 145,
    },
    trends: {
        hours: ['00:00', '01:00', '02:00', '03:00', '04:00', '05:00', '06:00', '07:00', '08:00', '09:00', '10:00', '11:00', '12:00', '13:00', '14:00', '15:00', '16:00', '17:00', '18:00', '19:00', '20:00', '21:00', '22:00', '23:00'],
        data: [45, 52, 38, 62, 55, 48, 71, 65, 79, 82, 68, 91, 105, 98, 112, 128, 95, 87, 72, 68, 55, 48, 42, 35],
    },
    departments: {
        'Infrastructure': { count: 342, percentage: 27 },
        'CRM': { count: 285, percentage: 23 },
        'Support': { count: 245, percentage: 20 },
        'Development': { count: 198, percentage: 16 },
        'Security': { count: 177, percentage: 14 },
    },
    tickets: [
        { id: 1, subject: 'Server connectivity issue on prod', from: 'john@company.com', timestamp: '2 mins ago', status: 'processing' },
        { id: 2, subject: 'CRM database sync failed', from: 'alice@company.com', timestamp: '5 mins ago', status: 'processing' },
        { id: 3, subject: 'Access denied for user account', from: 'bob@company.com', timestamp: '8 mins ago', status: 'completed' },
        { id: 4, subject: 'Email template generation issue', from: 'carol@company.com', timestamp: '12 mins ago', status: 'completed' },
        { id: 5, subject: 'API rate limiting exceeded', from: 'dave@company.com', timestamp: '15 mins ago', status: 'completed' },
        { id: 6, subject: 'Dashboard loading slowly', from: 'eve@company.com', timestamp: '18 mins ago', status: 'failed' },
    ],
};

// ========================================
// STATE MANAGEMENT
// ========================================

const STATE = {
    currentTab: 'pipeline',
    isPaused: false,
    selectedTicket: null,
    charts: {},
};

// ========================================
// UTILITY FUNCTIONS
// ========================================

/**
 * Animate a number from 0 to target
 */
function animateCounter(element, targetValue) {
    if (!CONFIG.countersEnabled) {
        element.textContent = targetValue;
        return;
    }

    const duration = CONFIG.animationDuration;
    const start = 0;
    const startTime = Date.now();

    function update() {
        const elapsed = Date.now() - startTime;
        const progress = Math.min(elapsed / duration, 1);
        const current = Math.floor(start + (targetValue - start) * progress);
        element.textContent = current;

        if (progress < 1) {
            requestAnimationFrame(update);
        } else {
            element.textContent = targetValue;
        }
    }

    update();
}

/**
 * Format numbers with thousands separator
 */
function formatNumber(num) {
    return num.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}

/**
 * Get data from API or use mock data
 */
async function fetchData(endpoint) {
    try {
        const response = await fetch(`${CONFIG.apiBaseUrl}${endpoint}`);
        if (response.ok) {
            return await response.json();
        }
    } catch (error) {
        console.warn(`Failed to fetch ${endpoint}, using mock data`, error);
    }

    // Fallback to mock data
    if (endpoint === '/stats') return MOCK_STATS;
    if (endpoint === '/tickets') return { tickets: MOCK_STATS.tickets };

    return null;
}

/**
 * Debounce function
 */
function debounce(func, delay) {
    let timeoutId;
    return function (...args) {
        clearTimeout(timeoutId);
        timeoutId = setTimeout(() => func(...args), delay);
    };
}

/**
 * Respect prefers-reduced-motion
 */
function getAnimationDuration() {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : CONFIG.animationDuration;
}

// ========================================
// HEADER & TIME
// ========================================

function initializeHeader() {
    const timeElement = document.getElementById('current-time');

    function updateTime() {
        const now = new Date();
        const hours = String(now.getHours()).padStart(2, '0');
        const minutes = String(now.getMinutes()).padStart(2, '0');
        const seconds = String(now.getSeconds()).padStart(2, '0');
        timeElement.textContent = `${hours}:${minutes}:${seconds}`;
    }

    updateTime();
    setInterval(updateTime, 1000);
}

// ========================================
// TAB MANAGEMENT
// ========================================

function initializeTabs() {
    const tabButtons = document.querySelectorAll('.tab-btn');
    const tabContents = document.querySelectorAll('.tab-content');

    tabButtons.forEach((btn) => {
        btn.addEventListener('click', () => {
            const tabName = btn.dataset.tab;

            // Update state
            STATE.currentTab = tabName;

            // Update button states
            tabButtons.forEach((b) => {
                b.classList.remove('active');
                b.setAttribute('aria-selected', 'false');
            });
            btn.classList.add('active');
            btn.setAttribute('aria-selected', 'true');

            // Update tab content
            tabContents.forEach((content) => {
                content.classList.remove('active');
            });
            document.getElementById(`${tabName}-tab`).classList.add('active');

            // Initialize charts if switching to metrics
            if (tabName === 'metrics') {
                setTimeout(initializeCharts, 100);
            }
        });
    });
}

// ========================================
// PIPELINE ORCHESTRATOR
// ========================================

async function initializePipeline() {
    const refreshBtn = document.getElementById('refresh-btn');
    const pauseToggle = document.getElementById('pause-toggle');

    // Refresh button
    refreshBtn.addEventListener('click', async () => {
        refreshBtn.disabled = true;
        refreshBtn.innerHTML = '<span class="btn-icon spinner"></span><span class="btn-text">Refreshing...</span>';

        await updatePipelineData();

        refreshBtn.disabled = false;
        refreshBtn.innerHTML = '<span class="btn-icon">🔄</span><span class="btn-text">Refresh</span>';
    });

    // Pause toggle
    pauseToggle.addEventListener('change', () => {
        STATE.isPaused = pauseToggle.checked;
    });

    // Initial data load
    await updatePipelineData();

    // Auto-refresh
    if (CONFIG.refreshInterval > 0) {
        setInterval(() => {
            if (!STATE.isPaused && STATE.currentTab === 'pipeline') {
                updatePipelineData();
            }
        }, CONFIG.refreshInterval);
    }
}

async function updatePipelineData() {
    const data = await fetchData('/stats');
    if (!data) return;

    // Update pipeline stages
    Object.entries(data.pipeline).forEach(([stage, count]) => {
        const stageElement = document.querySelector(`[data-stage="${stage}"]`);
        if (stageElement) {
            const counterElement = stageElement.querySelector('.counter-value');
            const targetValue = parseInt(counterElement.dataset.value) || count;
            animateCounter(counterElement, count);
            counterElement.dataset.value = count;
        }
    });

    // Update queue
    updateQueueCards(data.tickets || MOCK_STATS.tickets);
}

function updateQueueCards(tickets) {
    const queueContainer = document.getElementById('queue-container');

    tickets.forEach((ticket) => {
        let card = queueContainer.querySelector(`[data-ticket-id="${ticket.id}"]`);

        if (!card) {
            card = createQueueCard(ticket);
            queueContainer.insertBefore(card, queueContainer.firstChild);

            if (queueContainer.children.length > 10) {
                queueContainer.removeChild(queueContainer.lastChild);
            }
        }
    });
}

function createQueueCard(ticket) {
    const card = document.createElement('div');
    card.className = `queue-card status-${ticket.status}`;
    card.setAttribute('data-ticket-id', ticket.id);
    card.innerHTML = `
        <div class="queue-subject">${ticket.subject}</div>
        <div class="queue-from">${ticket.from}</div>
        <div class="queue-meta">
            <span class="queue-time">${ticket.timestamp}</span>
            <span class="status-badge ${ticket.status}">${ticket.status}</span>
        </div>
    `;

    card.addEventListener('click', () => {
        openDetailPanel(ticket);
    });

    return card;
}

// ========================================
// DETAIL PANEL
// ========================================

function openDetailPanel(ticket) {
    const detailPanel = document.getElementById('detail-panel');
    const detailContent = detailPanel.querySelector('.detail-content');

    detailContent.innerHTML = `
        <h2>${ticket.subject}</h2>
        <div class="detail-field">
            <div class="detail-label">From</div>
            <div class="detail-value">${ticket.from}</div>
        </div>
        <div class="detail-field">
            <div class="detail-label">Ticket ID</div>
            <div class="detail-value">#${ticket.id}</div>
        </div>
        <div class="detail-field">
            <div class="detail-label">Status</div>
            <div class="detail-value">
                <span class="status-badge ${ticket.status}">${ticket.status}</span>
            </div>
        </div>
        <div class="detail-field">
            <div class="detail-label">Received</div>
            <div class="detail-value">${ticket.timestamp}</div>
        </div>
        <div class="detail-field">
            <div class="detail-label">Processing Pipeline</div>
            <div class="detail-value">
                <ul style="list-style: none; padding: 0;">
                    <li>✓ Received</li>
                    <li>✓ AI Analysis Complete</li>
                    <li>✓ Routed to Team</li>
                    <li>${ticket.status === 'completed' ? '✓' : '→'} DevOps Integration</li>
                </ul>
            </div>
        </div>
    `;

    detailPanel.classList.add('open');
    document.body.style.overflow = 'hidden';
}

function closeDetailPanel() {
    const detailPanel = document.getElementById('detail-panel');
    detailPanel.classList.remove('open');
    document.body.style.overflow = '';
}

function setupDetailPanelHandlers() {
    const closeBtn = document.querySelector('.close-btn');
    closeBtn.addEventListener('click', closeDetailPanel);

    // Close on Escape key
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            closeDetailPanel();
        }
    });
}

// ========================================
// METRICS DASHBOARD
// ========================================

async function initializeMetrics() {
    await updateMetricsData();

    // Auto-refresh metrics
    if (CONFIG.refreshInterval > 0) {
        setInterval(() => {
            if (!STATE.isPaused && STATE.currentTab === 'metrics') {
                updateMetricsData();
            }
        }, CONFIG.refreshInterval);
    }
}

async function updateMetricsData() {
    const data = await fetchData('/stats');
    if (!data) return;

    // Update KPI cards
    Object.entries(data.kpis).forEach(([key, value]) => {
        const elements = document.querySelectorAll(`.kpi-number[data-value]`);
        elements.forEach((el) => {
            if (el.parentElement && el.parentElement.textContent.includes(value)) {
                animateCounter(el, value);
                el.dataset.value = value;
            }
        });
    });

    // Re-render all KPI numbers properly
    updateAllKPINumbers(data.kpis);
}

function updateAllKPINumbers(kpis) {
    const kpiCards = document.querySelectorAll('.kpi-card');

    kpiCards.forEach((card) => {
        const number = card.querySelector('.kpi-number');
        if (!number) return;

        const currentValue = parseInt(number.textContent) || 0;
        const label = card.querySelector('.kpi-label').textContent;

        // Map labels to data
        let targetValue = currentValue;
        if (label.includes('Total Processed')) targetValue = kpis.totalProcessed;
        else if (label.includes('In Queue')) targetValue = kpis.inQueue;
        else if (label.includes('Success Rate')) targetValue = kpis.successRate;
        else if (label.includes('Avg Processing')) targetValue = kpis.avgProcessingTime;
        else if (label.includes('AI Auto-Resolved')) targetValue = kpis.aiAutoResolved;
        else if (label.includes('Failed')) targetValue = kpis.failedTickets;
        else if (label.includes('System Uptime')) targetValue = kpis.systemUptime;
        else if (label.includes('API Response')) targetValue = kpis.apiResponseTime;

        if (targetValue !== currentValue) {
            animateCounter(number, targetValue);
        }
    });
}

// ========================================
// CHARTS
// ========================================

let trendChart = null;
let deptChart = null;

async function initializeCharts() {
    if (STATE.charts.initialized) return;

    const data = await fetchData('/stats');
    if (!data) return;

    createTrendChart(data.trends);
    createDepartmentChart(data.departments);

    STATE.charts.initialized = true;
}

function createTrendChart(trendData) {
    const ctx = document.getElementById('trend-chart');
    if (!ctx) return;

    // Destroy existing chart
    if (STATE.charts.trend) {
        STATE.charts.trend.destroy();
    }

    STATE.charts.trend = new Chart(ctx, {
        type: 'line',
        data: {
            labels: trendData.hours,
            datasets: [
                {
                    label: 'Emails Processed',
                    data: trendData.data,
                    borderColor: '#232D4B',
                    backgroundColor: 'rgba(35, 45, 75, 0.05)',
                    tension: 0.4,
                    fill: true,
                    pointRadius: 5,
                    pointBackgroundColor: '#F5F340',
                    pointBorderColor: '#232D4B',
                    pointBorderWidth: 2,
                    pointHoverRadius: 7,
                    borderWidth: 2,
                    padding: {
                        top: 20,
                    },
                },
            ],
        },
        options: {
            responsive: true,
            maintainAspectRatio: true,
            plugins: {
                legend: {
                    display: false,
                },
                tooltip: {
                    backgroundColor: 'rgba(0, 0, 0, 0.8)',
                    padding: 12,
                    titleFont: { weight: 600 },
                    bodyFont: { size: 13 },
                    borderColor: '#F5F340',
                    borderWidth: 1,
                },
            },
            scales: {
                y: {
                    beginAtZero: true,
                    grid: {
                        color: 'rgba(0, 0, 0, 0.05)',
                        drawBorder: false,
                    },
                    ticks: {
                        color: '#6B7280',
                        font: { size: 12 },
                    },
                },
                x: {
                    grid: {
                        display: false,
                    },
                    ticks: {
                        color: '#6B7280',
                        font: { size: 12 },
                    },
                },
            },
        },
    });
}

function createDepartmentChart(deptData) {
    const ctx = document.getElementById('dept-chart');
    if (!ctx) return;

    // Destroy existing chart
    if (STATE.charts.dept) {
        STATE.charts.dept.destroy();
    }

    const labels = Object.keys(deptData);
    const counts = labels.map((dept) => deptData[dept].count);
    const colors = [
        'rgba(35, 45, 75, 0.8)',
        'rgba(224, 32, 116, 0.8)',
        'rgba(74, 94, 140, 0.8)',
        'rgba(59, 130, 246, 0.8)',
        'rgba(245, 158, 11, 0.8)',
    ];

    STATE.charts.dept = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [
                {
                    label: 'Tickets',
                    data: counts,
                    backgroundColor: colors,
                    borderRadius: 8,
                    borderSkipped: false,
                },
            ],
        },
        options: {
            indexAxis: 'y',
            responsive: true,
            maintainAspectRatio: true,
            plugins: {
                legend: {
                    display: false,
                },
                tooltip: {
                    backgroundColor: 'rgba(0, 0, 0, 0.8)',
                    padding: 12,
                    titleFont: { weight: 600 },
                    bodyFont: { size: 13 },
                    callbacks: {
                        label: function (context) {
                            const value = context.parsed.x;
                            const total = context.dataset.data.reduce((a, b) => a + b, 0);
                            const percentage = ((value / total) * 100).toFixed(1);
                            return `${value} tickets (${percentage}%)`;
                        },
                    },
                },
            },
            scales: {
                x: {
                    grid: {
                        color: 'rgba(0, 0, 0, 0.05)',
                    },
                    ticks: {
                        color: '#6B7280',
                        font: { size: 12 },
                    },
                },
                y: {
                    ticks: {
                        color: '#6B7280',
                        font: { size: 12 },
                    },
                    grid: {
                        display: false,
                    },
                },
            },
        },
    });
}

// ========================================
// INITIALIZATION
// ========================================

function init() {
    // Check for required elements
    if (!document.getElementById('current-time')) {
        console.error('Dashboard elements not found');
        return;
    }

    initializeHeader();
    initializeTabs();
    setupDetailPanelHandlers();
    initializePipeline();
    initializeMetrics();

    console.log('Dashboard initialized successfully');
}

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
} else {
    init();
}

// ========================================
// EXPORT FOR API INTEGRATION
// ========================================

window.DashboardAPI = {
    updateData: async (stats) => {
        // Update with new data from API
        if (STATE.currentTab === 'pipeline') {
            updatePipelineData();
        } else {
            updateMetricsData();
        }
    },
    pauseAutoRefresh: () => {
        STATE.isPaused = true;
        document.getElementById('pause-toggle').checked = true;
    },
    resumeAutoRefresh: () => {
        STATE.isPaused = false;
        document.getElementById('pause-toggle').checked = false;
    },
    openTicketDetail: (ticketId) => {
        const ticket = MOCK_STATS.tickets.find((t) => t.id === ticketId);
        if (ticket) {
            openDetailPanel(ticket);
        }
    },
    closeTicketDetail: closeDetailPanel,
    setRefreshInterval: (ms) => {
        CONFIG.refreshInterval = ms;
    },
    getState: () => STATE,
    getConfig: () => CONFIG,
};
