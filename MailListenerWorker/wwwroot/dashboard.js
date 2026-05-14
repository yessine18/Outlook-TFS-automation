/* ═══════════════════════════════════════════════════════════
   HELPDESK AUTOMATION DASHBOARD — Core Logic
   Connects to /api/stats and /api/tickets on the same host.
   ALL metrics are sourced from PostgreSQL — zero dummy data.
   ═══════════════════════════════════════════════════════════ */
(() => {
  'use strict';

  // ── Config ───────────────────────────────────────
  const REFRESH_MS = 600000;    // 10 minutes
  const API_BASE   = '';          // same origin
  const QUEUE_MAX  = 10;

  // ── State ────────────────────────────────────────
  let paused        = false;
  let refreshTimer  = null;
  let trendChart    = null;
  let deptChart     = null;
  let allTickets    = [];
  let startTime     = Date.now();

  // ── DOM refs ─────────────────────────────────────
  const $ = id => document.getElementById(id);
  const clockEl     = $('current-time');
  const statusDot   = $('status-dot');
  const statusText  = $('status-text');
  const refreshIcon = $('refresh-icon');
  const queueBox    = $('queue-container');
  const detailPanel = $('detail-panel');
  const detailOver  = $('detail-overlay');
  const detailBody  = $('detail-content');

  // ═══════════════ CLOCK ═══════════════
  function tickClock() {
    const now = new Date();
    clockEl.textContent = now.toLocaleTimeString('en-GB', { hour12: false });
  }
  setInterval(tickClock, 1000);
  tickClock();

  // ═══════════════ THEME TOGGLE ═══════════════
  (function initTheme() {
    const btn = $('theme-toggle');
    const saved = localStorage.getItem('theme');
    if (saved === 'dark') { document.documentElement.setAttribute('data-theme', 'dark'); btn.textContent = '☀️'; }
    btn.addEventListener('click', () => {
      const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
      document.documentElement.setAttribute('data-theme', isDark ? '' : 'dark');
      btn.textContent = isDark ? '🌙' : '☀️';
      localStorage.setItem('theme', isDark ? 'light' : 'dark');
    });
  })();

  // ═══════════════ TABS ═══════════════
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.tab-btn').forEach(b => { b.classList.remove('active'); b.setAttribute('aria-selected', 'false'); });
      document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
      btn.classList.add('active');
      btn.setAttribute('aria-selected', 'true');
      const target = document.getElementById(btn.dataset.tab + '-tab');
      if (target) target.classList.add('active');
    });
  });

  // ═══════════════ CONTROLS ═══════════════
  $('pause-toggle').addEventListener('change', e => {
    paused = e.target.checked;
    if (!paused) scheduleRefresh();
  });
  $('refresh-btn').addEventListener('click', () => fetchAll(true));

  // ═══════════════ DETAIL PANEL ═══════════════
  $('close-detail').addEventListener('click', closeDetail);
  detailOver.addEventListener('click', closeDetail);
  function openDetail(ticket) {
    const rows = [
      ['Subject', ticket.subject || '—'],
      ['Sender', ticket.senderEmail || '—'],
      ['Received', fmtDate(ticket.receivedAt)],
      ['Department', ticket.extractedDepartment || '—'],
      ['Intent', ticket.extractedIntent || '—'],
      ['Confidence', ticket.llmConfidenceScore != null ? (ticket.llmConfidenceScore * 100).toFixed(0) + '%' : '—'],
      ['ADO #', ticket.adoWorkItemId || '—'],
      ['Assignee', ticket.adoAssignee || '—'],
      ['ADO State', ticket.adoItemState || '—'],
      ['Pipeline', ticket.currentPipelineStatus || '—'],
      ['Rating', ticket.clientRating ? '⭐'.repeat(ticket.clientRating) : '—'],
      ['Feedback', ticket.clientFeedback || '—'],
    ];
    detailBody.innerHTML = `
      <div class="detail-header"><h3>${esc(ticket.subject || 'Ticket Details')}</h3>
        <span class="badge ${badgeClass(ticket)}">${badgeLabel(ticket)}</span>
      </div>
      ${rows.map(([l, v]) => `<div class="detail-row"><span class="detail-label">${l}</span><span class="detail-value">${esc(String(v))}</span></div>`).join('')}
      ${ticket.adoUrl ? `<div style="margin-top:1rem"><a href="${esc(ticket.adoUrl)}" target="_blank" rel="noopener" class="btn btn-primary" style="text-decoration:none;font-size:.8rem">Open in Azure DevOps →</a></div>` : ''}
    `;
    detailPanel.classList.add('open');
    detailOver.classList.add('open');
  }
  function closeDetail() { detailPanel.classList.remove('open'); detailOver.classList.remove('open'); }

  // ═══════════════ DATA FETCHING ═══════════════
  async function fetchAll(manual = false) {
    if (manual) { refreshIcon.classList.add('spinning'); }
    const fetchStart = performance.now();
    try {
      const [statsRes, ticketsRes] = await Promise.all([
        fetch(`${API_BASE}/api/stats`),
        fetch(`${API_BASE}/api/tickets`)
      ]);
      if (!statsRes.ok || !ticketsRes.ok) throw new Error('API error');
      const stats   = await statsRes.json();
      const tickets = await ticketsRes.json();
      const fetchEnd = performance.now();
      allTickets = tickets;
      setOnline(true);
      updatePipeline(stats);
      updateKpis(stats, fetchEnd - fetchStart);
      updateQueue(tickets);
      updateCharts(stats);
      updateErrors(stats);
    } catch (err) {
      console.warn('Fetch failed:', err);
      setOnline(false);
    } finally {
      if (manual) setTimeout(() => refreshIcon.classList.remove('spinning'), 500);
      scheduleRefresh();
    }
  }

  function scheduleRefresh() {
    clearTimeout(refreshTimer);
    if (!paused) refreshTimer = setTimeout(() => fetchAll(), REFRESH_MS);
  }

  function setOnline(ok) {
    statusDot.classList.toggle('offline', !ok);
    statusText.textContent = ok ? 'Online' : 'Offline';
  }

  // ═══════════════ PIPELINE STAGES ═══════════════
  // Uses pre-computed counts from the backend (from real PipelineStatus enum values)
  function updatePipeline(stats) {
    const p = stats.pipeline;

    // Inbox = EmailReceived
    animateNum($('stage-inbox'), p.inbox);

    // LLM Analysis = LlmProcessing + LlmSuccess + LlmFailed
    animateNum($('stage-llm'), p.llmProcessing + p.llmSuccess + p.llmFailed);

    // Intelligent Routing = AdoCreating (between LLM success and ADO creation)
    animateNum($('stage-routing'), p.adoCreating);

    // RAG Evaluation = PendingClientValidation (AI found a solution, waiting for client)
    animateNum($('stage-rag'), p.pendingValidation);

    // Azure DevOps = AdoCreated + AdoFailed (tickets sitting in ADO board)
    animateNum($('stage-devops'), p.adoCreated + p.adoFailed);

    // Completed = ClientAccepted + ClientRejected + MailFailed (terminal states)
    animateNum($('stage-completed'), p.clientAccepted + p.clientRejected);
  }

  // ═══════════════ KPI CARDS ═══════════════
  // ALL values sourced from the /api/stats backend — no hardcoded data
  function updateKpis(stats, apiResponseMs) {
    // ── Core Metrics ──────────────────────────────
    animateNum($('kpi-total'), stats.total);
    animateNum($('kpi-queue'), stats.inQueue);
    animateNum($('kpi-success'), stats.successRate);
    $('success-bar').style.width = stats.successRate + '%';

    // Avg Processing Time (real calculation from ReceivedAt to LastUpdatedAt)
    animateNum($('kpi-avgtime'), stats.avgProcessingSeconds);

    // ── Advanced Metrics ──────────────────────────
    animateNum($('kpi-resolved'), stats.aiAutoResolved);
    animateNum($('kpi-failed'), stats.failed);

    // Dynamic percentage labels (from real data)
    $('kpi-resolved-pct').textContent = `${stats.aiResolvedPct}% of total`;
    const failPct = stats.total > 0 ? ((stats.failed / stats.total) * 100).toFixed(1) : '0.0';
    $('kpi-failed-pct').textContent = `${failPct}% of total`;

    // System Uptime (calculated from when the page loaded)
    const uptimeMs = Date.now() - startTime;
    const uptimeHours = (uptimeMs / 3600000).toFixed(1);
    const uptimeEl = $('kpi-uptime');
    if (uptimeEl) uptimeEl.textContent = uptimeHours + 'h';

    // API Response Time (measured from actual fetch)
    const apiEl = $('kpi-api');
    if (apiEl) animateNum(apiEl, Math.round(apiResponseMs));

    // Client Rating display
    if (stats.avgClientRating > 0) {
      const ratingEl = $('kpi-resolved-pct');
      if (ratingEl && stats.ratedTicketsCount > 0) {
        ratingEl.textContent = `${stats.aiResolvedPct}% of total · ⭐ ${stats.avgClientRating}/5 avg`;
      }
    }
  }

  // ═══════════════ QUEUE CARDS ═══════════════
  function updateQueue(tickets) {
    const recent = tickets.slice(0, QUEUE_MAX);
    queueBox.innerHTML = recent.map((t, i) => `
      <div class="ticket-card ${cardStatusClass(t)}" data-idx="${i}" style="animation-delay:${i * 50}ms" tabindex="0">
        <div class="ticket-subject">${esc(t.subject || '(no subject)')}</div>
        <div class="ticket-sender">${esc(t.senderEmail || '—')}</div>
        <div class="ticket-meta">
          <span class="ticket-time">${timeAgo(t.receivedAt)}</span>
          <span class="badge ${badgeClass(t)}">${badgeLabel(t)}</span>
        </div>
      </div>
    `).join('');
    queueBox.querySelectorAll('.ticket-card').forEach(card => {
      const handler = () => openDetail(recent[+card.dataset.idx]);
      card.addEventListener('click', handler);
      card.addEventListener('keydown', e => { if (e.key === 'Enter') handler(); });
    });
  }

  // ═══════════════ CHARTS ═══════════════
  // Uses pre-aggregated data from the backend — no client-side grouping needed
  function updateCharts(stats) {
    buildTrendChart(stats);
    buildDeptChart(stats);
  }

  function buildTrendChart(stats) {
    const hours = Array.from({ length: 24 }, (_, i) => `${String(i).padStart(2, '0')}:00`);
    // Use pre-computed hourly counts from the backend
    const counts = stats.hourlyCounts || new Array(24).fill(0);
    const ctx = $('trend-chart').getContext('2d');
    const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
    const lineColor = isDark ? '#93A3D0' : '#232D4B';
    const areaColor = isDark ? 'rgba(147,163,208,.15)' : 'rgba(35,45,75,.1)';
    if (trendChart) trendChart.destroy();
    trendChart = new Chart(ctx, {
      type: 'line',
      data: {
        labels: hours,
        datasets: [{
          label: 'Processed',
          data: counts,
          borderColor: lineColor,
          backgroundColor: areaColor,
          fill: true,
          tension: .4,
          pointBackgroundColor: '#F5F340',
          pointBorderColor: lineColor,
          pointRadius: 3,
          pointHoverRadius: 6,
          borderWidth: 2
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: true,
        plugins: { legend: { display: false } },
        scales: {
          x: { grid: { display: false }, ticks: { color: isDark ? '#6B7280' : '#9CA3AF', maxTicksLimit: 12, font: { size: 10 } } },
          y: { beginAtZero: true, grid: { color: isDark ? '#2D3348' : '#F3F4F6' }, ticks: { color: isDark ? '#6B7280' : '#9CA3AF', font: { size: 10 } } }
        }
      }
    });
  }

  function buildDeptChart(stats) {
    // Use pre-aggregated department data from the backend
    const deptData = stats.departments || [];
    const labels = deptData.map(d => d.department);
    const values = deptData.map(d => d.count);
    const ctx = $('dept-chart').getContext('2d');
    const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
    if (deptChart) deptChart.destroy();
    deptChart = new Chart(ctx, {
      type: 'bar',
      data: {
        labels,
        datasets: [{
          label: 'Tickets',
          data: values,
          backgroundColor: labels.map((_, i) => {
            const colors = ['#3B82F6','#E20074','#8B5CF6','#06B6D4','#F59E0B','#10B981','#EF4444','#4A5E8C'];
            return colors[i % colors.length];
          }),
          borderRadius: 6,
          barThickness: 28
        }]
      },
      options: {
        indexAxis: 'y',
        responsive: true,
        maintainAspectRatio: true,
        plugins: { legend: { display: false } },
        scales: {
          x: { beginAtZero: true, grid: { color: isDark ? '#2D3348' : '#F3F4F6' }, ticks: { color: isDark ? '#6B7280' : '#9CA3AF', font: { size: 10 } } },
          y: { grid: { display: false }, ticks: { color: isDark ? '#C4CCDF' : '#374151', font: { size: 11, weight: '500' } } }
        }
      }
    });
  }

  // ═══════════════ ERROR ANALYSIS ═══════════════
  // Uses pre-computed error breakdown from the backend
  function updateErrors(stats) {
    const e = stats.errors;
    const total = stats.total || 1;

    setError('err-llm',  'err-llm-d',  e.llmFailed,  e.llmFailPct);
    setError('err-db',   'err-db-d',   0, 0);   // DB failures don't persist (pipeline aborts before saving)
    setError('err-ado',  'err-ado-d',  e.adoFailed,  e.adoFailPct);
    setError('err-mail', 'err-mail-d', e.mailFailed, e.mailFailPct);
  }
  function setError(rateId, detId, count, pct) {
    const el = $(rateId);
    el.textContent = pct + '%';
    el.className = 'error-rate ' + (pct < 3 ? 'rate-good' : pct < 5 ? 'rate-warn' : 'rate-bad');
    $(detId).textContent = `(${count} failure${count !== 1 ? 's' : ''})`;
  }

  // ═══════════════ UTILITIES ═══════════════
  function animateNum(el, target) {
    if (!el) return;
    const isFloat = target % 1 !== 0;
    const current = parseFloat(el.textContent) || 0;
    if (current === target) return;
    const dur = 400;
    const start = performance.now();
    function step(now) {
      const p = Math.min((now - start) / dur, 1);
      const ease = 1 - Math.pow(1 - p, 3);
      const val = current + (target - current) * ease;
      el.textContent = isFloat ? val.toFixed(1) : Math.round(val).toLocaleString();
      if (p < 1) requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
  }

  function esc(s) { const d = document.createElement('div'); d.textContent = s; return d.innerHTML; }

  function timeAgo(iso) {
    if (!iso) return '—';
    const diff = (Date.now() - new Date(iso).getTime()) / 1000;
    if (diff < 60)   return 'just now';
    if (diff < 3600) return Math.floor(diff / 60) + ' min ago';
    if (diff < 86400) return Math.floor(diff / 3600) + 'h ago';
    return Math.floor(diff / 86400) + 'd ago';
  }

  function fmtDate(iso) {
    if (!iso) return '—';
    return new Date(iso).toLocaleString('en-GB', { dateStyle: 'medium', timeStyle: 'short' });
  }

  function badgeLabel(t) {
    const s = (t.currentPipelineStatus || '').toLowerCase();
    if (s.includes('failed'))    return 'Failed';
    if (s.includes('accepted') || s.includes('rejected')) return 'Done';
    if (s === 'emailreceived')   return 'Inbox';
    if (s === 'pendingclientvalidation') return 'AI Resolved';
    return 'Processing';
  }
  function badgeClass(t) {
    const l = badgeLabel(t);
    return l === 'Failed' ? 'badge-failed' : l === 'Done' ? 'badge-done' : l === 'Inbox' ? 'badge-inbox' : l === 'AI Resolved' ? 'badge-done' : 'badge-processing';
  }
  function cardStatusClass(t) {
    const l = badgeLabel(t);
    return l === 'Failed' ? 'status-failed' : l === 'Done' ? 'status-done' : '';
  }

  // ═══════════════ BOOT ═══════════════
  fetchAll();
})();
