import { onRequest as __r__id__js_onRequest } from "C:\\Users\\KenChami\\source\\repos\\MasidBaha\\frontend\\functions\\r\\[id].js"
import { onRequest as ____path___js_onRequest } from "C:\\Users\\KenChami\\source\\repos\\MasidBaha\\frontend\\functions\\[[path]].js"

export const routes = [
    {
      routePath: "/r/:id",
      mountPath: "/r",
      method: "",
      middlewares: [],
      modules: [__r__id__js_onRequest],
    },
  {
      routePath: "/:path*",
      mountPath: "/",
      method: "",
      middlewares: [],
      modules: [____path___js_onRequest],
    },
  ]