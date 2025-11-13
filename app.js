class ThemeManager {
  static STORAGE_KEY = "school-demo-theme";

  constructor(toggleBtn) {
    this.toggleBtn = toggleBtn;
    this.current = document.documentElement.getAttribute("data-theme") || "dark";
    this.init();
  }

  init() {
    const saved = localStorage.getItem(ThemeManager.STORAGE_KEY);
    if (saved) {
      this.current = saved;
      document.documentElement.setAttribute("data-theme", this.current);
    } else {
      document.documentElement.setAttribute("data-theme", this.current);
    }

    this.toggleBtn.addEventListener("click", () => this.toggle());
  }

  toggle() {
    this.current = this.current === "dark" ? "light" : "dark";
    document.documentElement.setAttribute("data-theme", this.current);
    localStorage.setItem(ThemeManager.STORAGE_KEY, this.current);
  }
}

class TabManager {
  constructor(navEl, containerEl) {
    this.nav = navEl;
    this.container = containerEl;
    this.buttons = Array.from(this.nav.querySelectorAll(".tab-btn"));
    this.pages = Array.from(this.container.querySelectorAll(".tab-page"));
    this.pill = this.createPill();
    this.activeId = this.buttons[0]?.dataset.tab;
    this.attachEvents();
    this.restoreFromHash();
    this.movePill();
  }

  createPill() {
    const pill = document.createElement("div");
    pill.className = "tab-nav-pill";
    this.nav.appendChild(pill);
    return pill;
  }

  attachEvents() {
    this.nav.addEventListener("click", (e) => {
      const btn = e.target.closest(".tab-btn");
      if (!btn) return;
      const id = btn.dataset.tab;
      this.setActive(id, true);
    });

    window.addEventListener("resize", () => this.movePill());

    window.addEventListener("hashchange", () => this.restoreFromHash());
  }

  restoreFromHash() {
    const hash = window.location.hash.replace("#", "");
    if (!hash) return;
    const button = this.buttons.find((b) => b.dataset.tab === hash);
    if (button) {
      this.setActive(hash, false);
    }
  }

  setActive(id, updateHash = false) {
    if (this.activeId === id) return;
    this.activeId = id;

    this.buttons.forEach((btn) => {
      btn.classList.toggle("active", btn.dataset.tab === id);
      btn.style.zIndex = btn.dataset.tab === id ? "2" : "1";
    });

    this.pages.forEach((page) => {
      page.classList.toggle("active", page.dataset.tabPanel === id);
    });

    this.movePill();

    if (updateHash) {
      history.pushState(null, "", `#${id}`);
    }
  }

  movePill() {
    const activeBtn = this.buttons.find((b) => b.dataset.tab === this.activeId);
    if (!activeBtn) return;
    const navRect = this.nav.getBoundingClientRect();
    const btnRect = activeBtn.getBoundingClientRect();
    const offsetLeft = btnRect.left - navRect.left;
    this.pill.style.width = `${btnRect.width}px`;
    this.pill.style.transform = `translateX(${offsetLeft}px)`;
  }
}

class CounterAnimator {
  constructor(selector) {
    this.targets = Array.from(document.querySelectorAll(selector));
    if (this.targets.length === 0) return;
    this.observer = new IntersectionObserver(this.handleIntersect.bind(this), {
      threshold: 0.5,
    });
    this.targets.forEach((t) => this.observer.observe(t));
  }

  handleIntersect(entries) {
    entries.forEach((entry) => {
      if (entry.isIntersecting) {
        this.animate(entry.target);
        this.observer.unobserve(entry.target);
      }
    });
  }

  animate(el) {
    const target = parseFloat(el.dataset.counter);
    const duration = 900;
    const start = performance.now();

    const step = (now) => {
      const progress = Math.min((now - start) / duration, 1);
      const value = target * this.easeOutQuad(progress);
      el.textContent = Number.isInteger(target) ? Math.round(value) : value.toFixed(1);
      if (progress < 1) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }

  easeOutQuad(t) {
    return t * (2 - t);
  }
}

class ProgressAnimator {
  constructor(selector) {
    this.bars = Array.from(document.querySelectorAll(selector));
    if (this.bars.length === 0) return;
    this.observer = new IntersectionObserver(this.handle.bind(this), { threshold: 0.4 });
    this.bars.forEach((b) => this.observer.observe(b));
  }

  handle(entries) {
    entries.forEach((entry) => {
      if (!entry.isIntersecting) return;
      const bar = entry.target;
      const fill = bar.querySelector(".progress-fill");
      const value = bar.dataset.progress || 0;
      fill.style.width = `${value}%`;
      this.observer.unobserve(bar);
    });
  }
}

class StudentManager {
  constructor(tableEl, detailsEl, searchInput, gradeSelect) {
    this.table = tableEl;
    this.detailsEl = detailsEl;
    this.searchInput = searchInput;
    this.gradeSelect = gradeSelect;

    this.data = this.generateMockStudents();
    this.filtered = [...this.data];

    this.renderTable();
    this.attachEvents();
  }

  generateMockStudents() {
    const names = [
      "آرین حسینی",
      "نگین محمدی",
      "پارسا احمدی",
      "آیلین رستگار",
      "متین کریمی",
      "هلنا ساعی",
      "سامان نیک‌فر",
      "ستایش امانی",
      "امیررضا قاسمی",
      "هانیه قربانی",
      "بردیا امینی",
      "کیانا نعمتی",
    ];

    return names.map((name, idx) => {
      const grade = 7 + (idx % 3);
      const avg = 14 + (Math.random() * 6);
      const status =
        avg >= 18 ? "good" : avg >= 16 ? "medium" : "weak";

      return {
        id: 2000 + idx,
        name,
        grade,
        avg: Number(avg.toFixed(2)),
        status,
        absence: Math.floor(Math.random() * 8),
        phone: "۰۹۱۲۳۴۵۶۷۸" + (idx % 10),
      };
    });
  }

  attachEvents() {
    this.searchInput.addEventListener("input", () => this.applyFilters());
    this.gradeSelect.addEventListener("change", () => this.applyFilters());

    this.table.tBodies[0].addEventListener("click", (e) => {
      const row = e.target.closest("tr");
      if (!row) return;
      const id = Number(row.dataset.id);
      const student = this.filtered.find((s) => s.id === id);
      if (student) this.showDetails(student);
    });
  }

  applyFilters() {
    const term = this.searchInput.value.trim();
    const grade = this.gradeSelect.value;

    this.filtered = this.data.filter((s) => {
      const matchGrade = grade === "all" || String(s.grade) === grade;
      const termNorm = term.replace(/\s/g, "");
      const nameNorm = s.name.replace(/\s/g, "");
      const idStr = String(s.id);
      const matchTerm =
        termNorm === "" ||
        nameNorm.includes(termNorm) ||
        idStr.includes(termNorm);

      return matchGrade && matchTerm;
    });

    this.renderTable();
    this.clearDetails();
  }

  renderTable() {
    const tbody = this.table.tBodies[0];
    tbody.innerHTML = "";
    this.filtered.forEach((s, idx) => {
      const tr = document.createElement("tr");
      tr.dataset.id = s.id;
      tr.innerHTML = `
        <td>${idx + 1}</td>
        <td>${s.name}</td>
        <td>پایه ${this.toPersianGrade(s.grade)}</td>
        <td>${s.avg}</td>
        <td>
          <span class="status-pill ${s.status}">
            ${this.statusLabel(s.status)}
          </span>
        </td>
      `;
      tbody.appendChild(tr);
    });
  }

  toPersianGrade(g) {
    const map = { 7: "هفتم", 8: "هشتم", 9: "نهم" };
    return map[g] || g;
  }

  statusLabel(status) {
    switch (status) {
      case "good":
        return "عالی";
      case "medium":
        return "قابل قبول";
      default:
        return "نیازمند پیگیری";
    }
  }

  showDetails(s) {
    this.detailsEl.classList.remove("empty");
    this.detailsEl.innerHTML = `
      <h4>${s.name}</h4>
      <dl>
        <dt>شماره دانش‌آموزی:</dt>
        <dd>${s.id}</dd>
        <dt>پایه تحصیلی:</dt>
        <dd>پایه ${this.toPersianGrade(s.grade)}</dd>
        <dt>میانگین کل:</dt>
        <dd>${s.avg}</dd>
        <dt>تعداد غیبت:</dt>
        <dd>${s.absence} جلسه</dd>
        <dt>وضعیت آموزشی:</dt>
        <dd>${this.statusLabel(s.status)}</dd>
        <dt>شماره تماس ولی:</dt>
        <dd>${s.phone}</dd>
      </dl>
    `;
  }

  clearDetails() {
    this.detailsEl.classList.add("empty");
    this.detailsEl.innerHTML = `<p>برای مشاهده جزئیات، روی یکی از دانش‌آموزان جدول کلیک کنید.</p>`;
  }
}

class TeacherManager {
  constructor(container) {
    this.container = container;
    this.data = this.mockTeachers();
    this.render();
  }

  mockTeachers() {
    return [
      { name: "مینا رفیعی", field: "ریاضی", code: "T-101", hours: "۲۰ ساعت", experience: "۸ سال" },
      { name: "حسین لطفی", field: "فیزیک", code: "T-102", hours: "۱۸ ساعت", experience: "۶ سال" },
      { name: "بهاره کریم", field: "ادبیات فارسی", code: "T-103", hours: "۲۲ ساعت", experience: "۱۰ سال" },
      { name: "شایان کوهی", field: "علوم تجربی", code: "T-104", hours: "۱۶ ساعت", experience: "۴ سال" },
      { name: "نازنین علیزاده", field: "زبان انگلیسی", code: "T-105", hours: "۱۹ ساعت", experience: "۷ سال" },
      { name: "محمود اعتمادی", field: "مطالعات اجتماعی", code: "T-106", hours: "۱۵ ساعت", experience: "۵ سال" },
    ];
  }

  render() {
    this.container.innerHTML = "";
    this.data.forEach((t) => {
      const card = document.createElement("div");
      card.className = "teacher-card";
      card.innerHTML = `
        <div class="teacher-main">
          <div class="teacher-avatar">${t.name[0]}</div>
          <div>
            <h4>${t.name}</h4>
            <span>${t.field}</span>
            <div class="teacher-meta">
              <span>کد: ${t.code}</span>
              <span>سابقه: ${t.experience}</span>
            </div>
          </div>
        </div>
      `;
      this.container.appendChild(card);
    });
  }
}

class ScheduleManager {
  constructor(container, gradeSelect) {
    this.container = container;
    this.gradeSelect = gradeSelect;
    this.data = this.mockSchedules();
    this.gradeSelect.addEventListener("change", () => this.render());
    this.render();
  }

  mockSchedules() {
    return {
      7: {
        days: ["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه"],
        hours: ["۸-۹", "۹-۱۰", "۱۰-۱۱", "۱۱-۱۲"],
        grid: [
          [
            { name: "ریاضی", teacher: "رفیعی", note: "فصل معادلات" },
            { name: "علوم", teacher: "کوهی", note: "آزمایشگاه" },
            { name: "قرآن", teacher: "روحانی", note: "حفظ سوره" },
            { name: "زبان", teacher: "علیزاده", note: "Speaking" },
          ],
          [
            { name: "ادبیات", teacher: "کریم", note: "انشا" },
            { name: "ریاضی", teacher: "رفیعی", note: "تمرین اضافی" },
            { name: "ورزش", teacher: "مرادی", note: "سالن" },
            { name: "علوم", teacher: "کوهی", note: "فیلم آموزشی" },
          ],
          [
            { name: "زبان", teacher: "علیزاده", note: "گرامر" },
            { name: "مطالعات", teacher: "اعتمادی", note: "ایران باستان" },
            { name: "ادبیات", teacher: "کریم", note: "شعر" },
            { name: "پرورشی", teacher: "شریفی", note: "مشاوره" },
          ],
          [
            { name: "ریاضی", teacher: "رفیعی", note: "آزمونک" },
            { name: "علوم", teacher: "کوهی", note: "بحث کلاسی" },
            { name: "هنر", teacher: "صفری", note: "نقاشی" },
            { name: "زبان", teacher: "علیزاده", note: "Listening" },
          ],
          [
            { name: "ورزش", teacher: "مرادی", note: "زمین باز" },
            { name: "ادبیات", teacher: "کریم", note: "املاء" },
            { name: "مطالعات", teacher: "اعتمادی", note: "نقشه خوانی" },
            { name: "تفکر", teacher: "حسینی", note: "کار گروهی" },
          ],
        ],
      },
      8: {
        days: ["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه"],
        hours: ["۸-۹", "۹-۱۰", "۱۰-۱۱", "۱۱-۱۲"],
        grid: [
          [
            { name: "ریاضی", teacher: "لطفی", note: "توان و ریشه" },
            { name: "فیزیک", teacher: "لطفی", note: "حرکت" },
            { name: "زبان", teacher: "علیزاده", note: "Reading" },
            { name: "علوم", teacher: "کوهی", note: "زمین‌شناسی" },
          ],
          [
            { name: "ادبیات", teacher: "کریم", note: "آرایه‌ها" },
            { name: "مطالعات", teacher: "اعتمادی", note: "جغرافیا" },
            { name: "ورزش", teacher: "مرادی", note: "بدنسازی" },
            { name: "تفکر", teacher: "حسینی", note: "مساله‌محوری" },
          ],
          [
            { name: "زبان", teacher: "علیزاده", note: "Project" },
            { name: "فیزیک", teacher: "لطفی", note: "آزمایش" },
            { name: "ادبیات", teacher: "کریم", note: "داستان" },
            { name: "پرورشی", teacher: "شریفی", note: "کارگاه" },
          ],
          [
            { name: "ریاضی", teacher: "لطفی", note: "حل تمرین" },
            { name: "علوم", teacher: "کوهی", note: "گزارش" },
            { name: "هنر", teacher: "صفری", note: "طراحی" },
            { name: "مطالعات", teacher: "اعتمادی", note: "بررسی موردی" },
          ],
          [
            { name: "ورزش", teacher: "مرادی", note: "فوتسال" },
            { name: "ادبیات", teacher: "کریم", note: "انشاء" },
            { name: "زبان", teacher: "علیزاده", note: "Vocabulary" },
            { name: "تفکر", teacher: "حسینی", note: "ارائه" },
          ],
        ],
      },
      9: {
        days: ["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه"],
        hours: ["۸-۹", "۹-۱۰", "۱۰-۱۱", "۱۱-۱۲"],
        grid: [
          [
            { name: "ریاضی", teacher: "رفیعی", note: "جمع‌بندی" },
            { name: "فیزیک", teacher: "لطفی", note: "الکتریسیته" },
            { name: "زبان", teacher: "علیزاده", note: "Speaking" },
            { name: "علوم", teacher: "کوهی", note: "زیست" },
          ],
          [
            { name: "ادبیات", teacher: "کریم", note: "متون" },
            { name: "مطالعات", teacher: "اعتمادی", note: "مدنی" },
            { name: "ورزش", teacher: "مرادی", note: "آزاد" },
            { name: "تفکر", teacher: "حسینی", note: "کنکور" },
          ],
          [
            { name: "زبان", teacher: "علیزاده", note: "Mock Test" },
            { name: "فیزیک", teacher: "لطفی", note: "آزمایش" },
            { name: "ادبیات", teacher: "کریم", note: "آزمون" },
            { name: "پرورشی", teacher: "شریفی", note: "مشاوره تحصیلی" },
          ],
          [
            { name: "ریاضی", teacher: "رفیعی", note: "تست‌زنی" },
            { name: "علوم", teacher: "کوهی", note: "مرور" },
            { name: "هنر", teacher: "صفری", note: "خلاقیت" },
            { name: "مطالعات", teacher: "اعتمادی", note: "ایران معاصر" },
          ],
          [
            { name: "ورزش", teacher: "مرادی", note: "آمادگی" },
            { name: "ادبیات", teacher: "کریم", note: "شعر معاصر" },
            { name: "زبان", teacher: "علیزاده", note: "Listening" },
            { name: "تفکر", teacher: "حسینی", note: "مدیریت زمان" },
          ],
        ],
      },
    };
  }

  render() {
    const grade = this.gradeSelect.value || "7";
    const cfg = this.data[grade];
    if (!cfg) return;

    this.container.innerHTML = "";

    const headers = ["", ...cfg.days];
    headers.forEach((label, colIdx) => {
      const slot = document.createElement("div");
      slot.className = "slot header";
      if (colIdx === 0) {
        slot.innerHTML = `<span>ساعت</span>`;
      } else {
        slot.innerHTML = `<span>${label}</span>`;
      }
      this.container.appendChild(slot);
    });

    cfg.hours.forEach((hour, rowIdx) => {
      const hourSlot = document.createElement("div");
      hourSlot.className = "slot header";
      hourSlot.innerHTML = `<span>${hour}</span>`;
      this.container.appendChild(hourSlot);

      cfg.days.forEach((_, dayIdx) => {
        const lesson = cfg.grid[dayIdx]?.[rowIdx];
        const slot = document.createElement("div");
        slot.className = "slot lesson";
        if (lesson) {
          slot.innerHTML = `
            <div class="lesson-name">${lesson.name}</div>
            <div class="lesson-teacher">${lesson.teacher}</div>
            <div class="lesson-tooltip">${lesson.note}</div>
          `;
        } else {
          slot.textContent = "-";
        }
        this.container.appendChild(slot);
      });
    });
  }
}

class SimpleCharts {
  static drawBarChart(canvas, labels, values, options = {}) {
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    ctx.scale(dpr, dpr);

    const width = rect.width;
    const height = rect.height;
    ctx.clearRect(0, 0, width, height);

    const padding = 30;
    const chartWidth = width - padding * 2;
    const chartHeight = height - padding * 2;
    const maxVal = Math.max(...values) * 1.1;

    ctx.strokeStyle = "rgba(148,163,184,0.7)";
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(padding, padding);
    ctx.lineTo(padding, padding + chartHeight);
    ctx.lineTo(padding + chartWidth, padding + chartHeight);
    ctx.stroke();

    const barWidth = chartWidth / (values.length * 1.6);
    const gap = barWidth * 0.6;
    const startX = padding + gap;

    values.forEach((val, i) => {
      const heightRatio = val / maxVal;
      const barHeight = chartHeight * heightRatio;
      const x = startX + i * (barWidth + gap);
      const y = padding + chartHeight - barHeight;

      const grad = ctx.createLinearGradient(x, y, x, y + barHeight);
      grad.addColorStop(0, options.colorStart || "#6366f1");
      grad.addColorStop(1, options.colorEnd || "#ec4899");

      ctx.fillStyle = grad;
      ctx.beginPath();
      const radius = 6;
      ctx.moveTo(x, y + barHeight);
      ctx.lineTo(x, y + radius);
      ctx.quadraticCurveTo(x, y, x + radius, y);
      ctx.lineTo(x + barWidth - radius, y);
      ctx.quadraticCurveTo(x + barWidth, y, x + barWidth, y + radius);
      ctx.lineTo(x + barWidth, y + barHeight);
      ctx.closePath();
      ctx.fill();

      ctx.fillStyle = "rgba(148,163,184,0.9)";
      ctx.font = "10px Vazirmatn";
      ctx.textAlign = "center";
      ctx.fillText(labels[i], x + barWidth / 2, padding + chartHeight + 14);

      ctx.fillStyle = "rgba(248,250,252,0.9)";
      ctx.fillText(val, x + barWidth / 2, y - 4);
    });
  }

  static drawLineChart(canvas, labels, values) {
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    ctx.scale(dpr, dpr);

    const width = rect.width;
    const height = rect.height;
    ctx.clearRect(0, 0, width, height);

    const padding = 30;
    const chartWidth = width - padding * 2;
    const chartHeight = height - padding * 2;
    const maxVal = Math.max(...values) * 1.1;

    ctx.strokeStyle = "rgba(148,163,184,0.7)";
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(padding, padding);
    ctx.lineTo(padding, padding + chartHeight);
    ctx.lineTo(padding + chartWidth, padding + chartHeight);
    ctx.stroke();

    const stepX = chartWidth / (values.length - 1);
    const points = values.map((val, i) => {
      const x = padding + i * stepX;
      const ratio = val / maxVal;
      const y = padding + chartHeight - chartHeight * ratio;
      return { x, y };
    });

    const gradient = ctx.createLinearGradient(
      padding,
      padding,
      padding + chartWidth,
      padding + chartHeight
    );
    gradient.addColorStop(0, "#4f46e5");
    gradient.addColorStop(1, "#22c55e");

    ctx.strokeStyle = gradient;
    ctx.lineWidth = 2;
    ctx.beginPath();
    points.forEach((p, idx) => {
      if (idx === 0) ctx.moveTo(p.x, p.y);
      else ctx.lineTo(p.x, p.y);
    });
    ctx.stroke();

    ctx.fillStyle = "rgba(79,70,229,0.16)";
    ctx.beginPath();
    ctx.moveTo(points[0].x, padding + chartHeight);
    points.forEach((p) => ctx.lineTo(p.x, p.y));
    ctx.lineTo(points[points.length - 1].x, padding + chartHeight);
    ctx.closePath();
    ctx.fill();

    points.forEach((p, i) => {
      ctx.beginPath();
      ctx.fillStyle = "#e5e7eb";
      ctx.arc(p.x, p.y, 3, 0, Math.PI * 2);
      ctx.fill();

      ctx.fillStyle = "rgba(148,163,184,0.9)";
      ctx.font = "10px Vazirmatn";
      ctx.textAlign = "center";
      ctx.fillText(labels[i], p.x, padding + chartHeight + 14);
    });
  }
}

class NotificationCenter {
  constructor(form, timeline) {
    this.form = form;
    this.timeline = timeline;
    this.attachEvents();
  }

  attachEvents() {
    this.form.addEventListener("submit", (e) => {
      e.preventDefault();
      const title = this.form.querySelector("#notifTitle").value.trim();
      const body = this.form.querySelector("#notifMessage").value.trim();
      const audience = this.form.querySelector("#notifAudience").value;

      if (!title || !body) return;

      this.pushNotification({ title, body, audience });
      this.form.reset();
    });
  }

  pushNotification({ title, body, audience }) {
    const li = document.createElement("li");
    li.className = "notification-item";
    const audienceLabel =
      audience === "all"
        ? "همه کاربران"
        : audience === "parents"
        ? "اولیا"
        : "دانش‌آموزان";

    const timeStr = new Date().toLocaleTimeString("fa-IR", {
      hour: "2-digit",
      minute: "2-digit",
    });

    li.innerHTML = `
      <div class="notification-title">${title}</div>
      <div class="notification-meta">${audienceLabel} • ${timeStr}</div>
      <div class="notification-body">${body}</div>
    `;

    this.timeline.prepend(li);
  }
}

function animateOnScroll() {
  const cards = document.querySelectorAll(".card");
  const observer = new IntersectionObserver(
    (entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          entry.target.style.transform = "translateY(0)";
          entry.target.style.opacity = "1";
          observer.unobserve(entry.target);
        }
      });
    },
    { threshold: 0.25 }
  );

  cards.forEach((card, idx) => {
    card.style.opacity = "0";
    card.style.transform = "translateY(8px)";
    card.style.transition = `opacity 280ms ease-out ${idx * 15}ms, transform 280ms ease-out ${idx * 15}ms`;
    observer.observe(card);
  });
}

document.addEventListener("DOMContentLoaded", () => {
  const themeToggle = document.getElementById("themeToggle");
  new ThemeManager(themeToggle);

  const tabNav = document.getElementById("tabNav");
  const tabContainer = document.querySelector(".tab-container");
  new TabManager(tabNav, tabContainer);

  new CounterAnimator(".stat-value");
  new ProgressAnimator(".progress-bar");

  const studentsTable = document.getElementById("studentsTable");
  const studentDetails = document.getElementById("studentDetails");
  const searchInput = document.getElementById("studentSearch");
  const gradeFilter = document.getElementById("gradeFilter");
  new StudentManager(studentsTable, studentDetails, searchInput, gradeFilter);

  const teachersGrid = document.getElementById("teachersGrid");
  new TeacherManager(teachersGrid);

  const scheduleGrid = document.getElementById("scheduleGrid");
  const scheduleGrade = document.getElementById("scheduleGrade");
  new ScheduleManager(scheduleGrid, scheduleGrade);

  const avgChart = document.getElementById("avgChart");
  const attendanceChart = document.getElementById("attendanceChart");
  SimpleCharts.drawBarChart(
    avgChart,
    ["هفتم", "هشتم", "نهم"],
    [18.2, 17.6, 18.9]
  );
  SimpleCharts.drawLineChart(
    attendanceChart,
    ["۱", "۵", "۱۰", "۱۵", "۲۰", "۲۵"],
    [92, 94, 90, 95, 93, 96]
  );

  const notificationForm = document.getElementById("notificationForm");
  const notificationsTimeline = document.getElementById("notificationsTimeline");
  new NotificationCenter(notificationForm, notificationsTimeline);

  animateOnScroll();

  document.querySelectorAll("[data-demo-action='tour']").forEach((btn) => {
    btn.addEventListener("click", () => {
      const nav = document.getElementById("tabNav");
      const sequence = ["students", "teachers", "schedule", "reports", "notifications"];
      sequence.forEach((tabId, idx) => {
        setTimeout(() => {
          const button = nav.querySelector(`[data-tab="${tabId}"]`);
          if (button) button.click();
        }, idx * 650);
      });

      setTimeout(() => {
        const button = nav.querySelector(`[data-tab="overview"]`);
        if (button) button.click();
      }, sequence.length * 650 + 400);
    });
  });
});
