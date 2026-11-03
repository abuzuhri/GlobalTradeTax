const fs = require("node:fs");
const path = require("node:path");
const esbuild = require("esbuild");

const result = esbuild.buildSync({ entryPoints: ["SaleorApp.js"], bundle: true, format: "iife", minify: true,
  outfile: "SaleorApp.bundle.js", metafile: true });
const packages = new Set();
for (const input of Object.keys(result.metafile.inputs)) {
  const pieces = input.split("/");
  if (pieces[0] !== "node_modules") continue;
  packages.add(pieces[1].startsWith("@") ? pieces.slice(1, 3).join("/") : pieces[1]);
}
const notices = ["Third-party notices for the GlobalTradeTax Saleor review UI.\n"];
for (const name of [...packages].sort()) {
  const directory = path.join("node_modules", name);
  const license = fs.readdirSync(directory).find(file => /^licen[sc]e(?:\..*)?$/i.test(file));
  if (!license) throw new Error(`License file missing for bundled dependency ${name}`);
  notices.push(`\n===== ${name} =====\n\n` + fs.readFileSync(path.join(directory, license), "utf8").replace(/\r\n/g, "\n"));
}
fs.writeFileSync("SaleorApp.NOTICES.txt", notices.join("\n"));
