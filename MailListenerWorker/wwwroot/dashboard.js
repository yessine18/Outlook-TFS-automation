/* ═══════════════════════════════════════════════════════════
   HELPDESK AUTOMATION DASHBOARD — Core Logic
   Connects to /api/stats and /api/tickets on the same host.
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
    try {
      const [statsRes, ticketsRes] = await Promise.all([
        fetch(`${API_BASE}/api/stats`),
        fetch(`${API_BASE}/api/tickets`)
      ]);
      if (!statsRes.ok || !ticketsRes.ok) throw new Error('API error');
      const stats   = await statsRes.json();
      const tickets = await ticketsRes.json();
      allTickets = tickets;
      setOnline(true);
      updatePipeline(stats, tickets);
      updateKpis(stats, tickets);
      updateQueue(tickets);
      updateCharts(tickets);
      updateErrors(stats, tickets);
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
  function updatePipeline(stats, tickets) {
    const counts = { inbox: 0, llm: 0, routing: 0, rag: 0, devops: 0, completed: 0 };
    tickets.forEach(t => {
      const s = (t.currentPipelineStatus || '').toLowerCase();
      if (s === 'emailreceived')                                    counts.inbox++;
      else if (s === 'llmsuccess' || s === 'llmfailed')             counts.llm++;
      else if (s === 'routedtoassignee')                            counts.routing++;
      else if (s === 'ragresolved' || s === 'ragfailed')            counts.rag++;
      else if (s === 'adocreated' || s === 'adofailed')             counts.devops++;
      else                                                          counts.completed++;
    });
    animateNum($('stage-inbox'), counts.inbox);
    animateNum($('stage-llm'), counts.llm);
    animateNum($('stage-routing'), counts.routing);
    animateNum($('stage-rag'), counts.rag);
    animateNum($('stage-devops'), counts.devops);
    animateNum($('stage-completed'), counts.completed);
  }

  // ═══════════════ KPI CARDS ═══════════════
  function updateKpis(stats, tickets) {
    const total     = stats.total || 0;
    const processed = stats.processed || 0;
    const failed    = stats.failed || 0;
    const success   = total > 0 ? ((processed / total) * 100) : 0;
    const resolved  = tickets.filter(t => {
      const s = (t.currentPipelineStatus || '').toLowerCase();
      return s === 'ragresolved' || s === 'clientacceptedresolution';
    }).length;
    const inQueue   = tickets.filter(t => {
      const s = (t.currentPipelineStatus || '').toLowerCase();
      return s === 'emailreceived' || s === 'llmsuccess';
    }).length;

    animateNum($('kpi-total'), total);
    animateNum($('kpi-queue'), inQueue);
    animateNum($('kpi-success'), parseFloat(success.toFixed(1)));
    animateNum($('kpi-avgtime'), 2.3);
    animateNum($('kpi-resolved'), resolved);
    animateNum($('kpi-failed'), failed);

    $('success-bar').style.width = success.toFixed(1) + '%';
    const failPct = total > 0 ? ((failed / total) * 100).toFixed(1) : 0;
    const resPct  = total > 0 ? ((resolved / total) * 100).toFixed(0) : 0;
    $('kpi-failed-pct').textContent = `${failPct}% of total`;
    $('kpi-resolved-pct').textContent = `${resPct}% of total`;
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
  function updateCharts(tickets) {
    buildTrendChart(tickets);
    buildDeptChart(tickets);
  }

  function buildTrendChart(tickets) {
    const hours = Array.from({ length: 24 }, (_, i) => `${String(i).padStart(2, '0')}:00`);
    const counts = new Array(24).fill(0);
    tickets.forEach(t => {
      if (!t.receivedAt) return;
      const h = new Date(t.receivedAt).getHours();
      counts[h]++;
    });
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

  function buildDeptChart(tickets) {
    const deptMap = {};
    tickets.forEach(t => {
      const d = t.extractedDepartment || 'Unclassified';
      deptMap[d] = (deptMap[d] || 0) + 1;
    });
    const sorted = Object.entries(deptMap).sort((a, b) => b[1] - a[1]).slice(0, 8);
    const labels = sorted.map(e => e[0]);
    const values = sorted.map(e => e[1]);
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
  function updateErrors(stats, tickets) {
    const total = stats.total || 1;
    const failed = stats.failed || 0;
    // Approximate breakdown
    const llmFail  = tickets.filter(t => (t.currentPipelineStatus || '').toLowerCase() === 'llmfailed').length;
    const adoFail  = tickets.filter(t => (t.currentPipelineStatus || '').toLowerCase() === 'adofailed').length;
    const mailFail = tickets.filter(t => (t.currentPipelineStatus || '').toLowerCase() === 'mailsendingfailed').length;
    const dbFail   = Math.max(0, failed - llmFail - adoFail - mailFail);

    setError('err-llm',  'err-llm-d',  llmFail, total);
    setError('err-db',   'err-db-d',   dbFail, total);
    setError('err-ado',  'err-ado-d',  adoFail, total);
    setError('err-mail', 'err-mail-d', mailFail, total);
  }
  function setError(rateId, detId, count, total) {
    const pct = total > 0 ? ((count / total) * 100).toFixed(1) : 0;
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
    if (s.includes('done') || s.includes('closed') || s.includes('accepted')) return 'Done';
    if (s === 'emailreceived')   return 'Inbox';
    return 'Processing';
  }
  function badgeClass(t) {
    const l = badgeLabel(t);
    return l === 'Failed' ? 'badge-failed' : l === 'Done' ? 'badge-done' : l === 'Inbox' ? 'badge-inbox' : 'badge-processing';
  }
  function cardStatusClass(t) {
    const l = badgeLabel(t);
    return l === 'Failed' ? 'status-failed' : l === 'Done' ? 'status-done' : '';
  }

  // ═══════════════ BOOT ═══════════════
  fetchAll();
})();
