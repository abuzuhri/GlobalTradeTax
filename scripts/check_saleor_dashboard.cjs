const fs = require("node:fs");
const path = require("node:path");
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || "playwright");

async function main() {
  const results = path.resolve(process.argv[2]);
  const root = path.resolve(__dirname, "..");
  const settings = Object.fromEntries(fs.readFileSync(path.join(root, "dev/saleor/.env"), "utf8").split(/\r?\n/).filter(line => line.includes("=")).map(line => {
    const separator = line.indexOf("=");
    return [line.slice(0, separator), line.slice(separator + 1)];
  }));
  const summary = JSON.parse(fs.readFileSync(path.join(results, "saleor-app-summary.json"), "utf8"));
  const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROME_PATH || undefined });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1080 } });
    await page.route("**/*", route => {
      const address = new URL(route.request().url());
      return ["localhost", "127.0.0.1"].includes(address.hostname) ? route.continue() : route.abort();
    });
    await page.goto("http://localhost:19090/", { waitUntil: "domcontentloaded" });
    await page.getByLabel("Email address", { exact: true }).fill(settings.SANDBOX_ADMIN_EMAIL);
    await page.getByLabel("Password", { exact: true }).fill(settings.SANDBOX_ADMIN_PASSWORD);
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await page.getByLabel("Email address", { exact: true }).waitFor({ state: "hidden", timeout: 30000 });
    await page.goto(summary.dashboardApp, { waitUntil: "domcontentloaded" });
    const deadline = Date.now() + 30000;
    let reviewFrame;
    while (Date.now() < deadline) {
      reviewFrame = page.frames().find(frame => frame.url().startsWith("http://localhost:18081/saleor/sandbox/app"));
      if (reviewFrame) break;
      await page.waitForTimeout(200);
    }
    if (!reviewFrame) {
      await page.screenshot({ path: path.join(results, "saleor-dashboard-debug.png"), fullPage: true });
      process.stdout.write(JSON.stringify({ url: page.url(), text: (await page.locator("body").innerText()).slice(0, 1800), frames: page.frames().map(frame => frame.url()) }) + "\n");
      throw new Error("Dashboard did not load the installed review App iframe");
    }
    await reviewFrame.getByRole("heading", { name: "مراجعات السلال الجمركية", exact: true }).waitFor();
    await reviewFrame.locator(".review").first().waitFor({ timeout: 30000 });
    const cards = await reviewFrame.locator(".review").count();
    if (cards < 2) throw new Error("Expected automatically delivered created/updated reviews");
    const body = await reviewFrame.locator("body").innerText();
    if (!body.includes("6109100000") || !body.includes("CN") || !body.includes("التحصيل غير مفعّل")) throw new Error("Review UI lost source metadata or non-payable warning");
    const promotion = page.getByText("Saleor Pulse", { exact: true });
    if (await promotion.count()) {
      const close = promotion.locator("..").getByRole("button");
      if (await close.count() === 1) await close.click();
    }
    await page.screenshot({ path: path.join(results, "saleor-dashboard-review.png"), fullPage: true });
    const result = { actualDashboardLogin: true, installedAppIframeLoaded: true, appBridgeAuthenticated: true,
      displayedReviewCards: cards, customsMetadataVisible: true, collectionDisabledWarningVisible: true };
    fs.writeFileSync(path.join(results, "saleor-dashboard-ui-results.json"), JSON.stringify(result, null, 2));
    process.stdout.write(JSON.stringify(result, null, 2) + "\n");
  } finally {
    await browser.close();
  }
}

main().catch(error => { process.stderr.write(error.message + "\n"); process.exitCode = 1; });
