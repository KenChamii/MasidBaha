// <define:__ROUTES__>
var define_ROUTES_default = {
  version: 1,
  include: ["/api/*", "/hubs/*", "/uploads/*", "/r/*"],
  exclude: []
};

// ../../../../AppData/Local/npm-cache/_npx/32026684e21afda6/node_modules/wrangler/templates/pages-dev-pipeline.ts
import worker from "C:\\Users\\KenChami\\source\\repos\\MasidBaha\\frontend\\.wrangler\\tmp\\pages-i8hEGT\\functionsWorker-0.9357009943513781.mjs";
import { isRoutingRuleMatch } from "C:\\Users\\KenChami\\AppData\\Local\\npm-cache\\_npx\\32026684e21afda6\\node_modules\\wrangler\\templates\\pages-dev-util.ts";
export * from "C:\\Users\\KenChami\\source\\repos\\MasidBaha\\frontend\\.wrangler\\tmp\\pages-i8hEGT\\functionsWorker-0.9357009943513781.mjs";
var routes = define_ROUTES_default;
var pages_dev_pipeline_default = {
  fetch(request, env, context) {
    const { pathname } = new URL(request.url);
    for (const exclude of routes.exclude) {
      if (isRoutingRuleMatch(pathname, exclude)) {
        return env.ASSETS.fetch(request);
      }
    }
    for (const include of routes.include) {
      if (isRoutingRuleMatch(pathname, include)) {
        const workerAsHandler = worker;
        if (workerAsHandler.fetch === void 0) {
          throw new TypeError("Entry point missing `fetch` handler");
        }
        return workerAsHandler.fetch(request, env, context);
      }
    }
    return env.ASSETS.fetch(request);
  }
};
export {
  pages_dev_pipeline_default as default
};
//# sourceMappingURL=yw6v2fj6zoo.js.map
