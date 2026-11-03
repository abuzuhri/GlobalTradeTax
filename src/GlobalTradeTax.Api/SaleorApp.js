import { AppBridge } from "@saleor/app-sdk/app-bridge";

const statusElement = document.getElementById("status");
const container = document.getElementById("reviews");
const button = document.getElementById("refresh");
const bridge = new AppBridge({ saleorApiUrl: document.body.dataset.apiUrl });
const labels = {
  review_only_not_a_saleor_tax_response: "هذه مراجعة معلومات، وليست نتيجة ضريبية للتحصيل.",
  authoritative_duty_and_vat_not_verified: "يلزم التحقق من الرسوم وVAT من مصدر معتمد.",
  ship_from_and_incoterm_require_mapping: "يلزم تحديد بلد الشحن وشروط DDP أو DAP.",
  discount_shipping_and_rounding_allocation_not_verified: "توزيع الخصومات والشحن والتقريب لم يعتمد بعد.",
  destination_country_missing: "بلد الوجهة غير موجود في السلة.",
  asynchronous_event_not_a_checkout_quote: "سُجّل هذا الحدث للمراجعة، دون إصدار عرض دفع."
};

function element(tag, text, className) {
  const node = document.createElement(tag);
  if (text !== undefined) node.textContent = text;
  if (className) node.className = className;
  return node;
}

function render(entries) {
  container.replaceChildren();
  for (const entry of entries) {
    const review = entry.review;
    const card = element("article", undefined, "review");
    const header = element("header");
    header.append(element("h3", `سلة ${review.channel} · ${review.currency}`), element("span", Date.now() > Date.parse(review.expiresAt) ? "مرجع سابق — يحتاج تحديثًا" : "تحتاج مراجعة", "badge"));
    card.append(header, element("p", review.reference, "reference"));
    const facts = element("div", undefined, "facts");
    facts.append(element("span", `الحدث: ${entry.eventType}`), element("span", `الوجهة: ${review.destinationCountry || "غير محددة"}`),
      element("span", new Date(review.sourceIssuedAt).toLocaleString("ar")));
    card.append(facts);
    const table = element("table");
    const headings = element("tr");
    for (const name of ["المنتج", "الكمية", "رمز HS", "المنشأ", "سعر السطر المصدر"]) headings.append(element("th", name));
    table.append(headings);
    for (const line of review.lines) {
      const row = element("tr");
      for (const value of [line.sku || "غير محدد", line.quantity, line.hsCode || "ناقص", line.countryOfOrigin || "ناقص", `${line.sourceTotalAmount} ${review.currency}`]) row.append(element("td", String(value)));
      table.append(row);
    }
    card.append(table, element("h4", "نقاط المراجعة", "checks-title"));
    const checks = element("ul");
    for (const check of review.unresolvedChecks) {
      const prefix = check.split(":")[0];
      const label = labels[check] || (prefix === "classification_and_origin_unverified" ? "التصنيف وبلد المنشأ يحتاجان إلى تحقق مستقل." :
        prefix === "hs_code_missing_or_invalid" ? "رمز HS ناقص أو غير صالح." : prefix === "origin_missing_or_invalid" ? "بلد المنشأ ناقص أو غير صالح." :
        prefix === "sku_missing" ? "معرّف المنتج ناقص." : check);
      checks.append(element("li", label));
    }
    card.append(checks, element("p", "الجمارك: غير معتمدة · VAT: غير معتمدة · الإجمالي الجمركي: غير متاح", "footer"));
    container.append(card);
  }
}

async function refresh() {
  const token = bridge.getState().token;
  if (!token) return;
  button.disabled = true;
  try {
    const response = await fetch(location.pathname.replace(/\/app$/, "/reviews"), { headers: { Authorization: `Bearer ${token}` }, cache: "no-store" });
    if (response.status === 403) throw new Error("هذا التثبيت معطّل أو أُلغي. فعّل التطبيق في المتجر أو أعد تثبيته لعرض المراجعات.");
    if (!response.ok) throw new Error("تعذر تحميل المراجعات. تحقق من صلاحيات المستخدم واتصال المتجر.");
    const result = await response.json();
    render(result.reviews);
    statusElement.textContent = result.reviews.length ? `${result.reviews.length} حدثًا موثّقًا. المبالغ الجمركية غير قابلة للتحصيل.` : "لا توجد أحداث بعد. أنشئ سلة تجريبية أو حدّثها في المتجر.";
  } catch (error) {
    container.replaceChildren();
    statusElement.textContent = error.message;
  } finally {
    button.disabled = false;
  }
}

bridge.subscribe("handshake", refresh);
bridge.subscribe("tokenRefresh", refresh);
button.addEventListener("click", refresh);
if (bridge.getState().token) refresh();
