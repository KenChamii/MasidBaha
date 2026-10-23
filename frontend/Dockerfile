# One image per Angular app. Build context is the frontend/ folder:
#   docker build --build-arg PROJECT=web   -t masidbaha-web   frontend
#   docker build --build-arg PROJECT=admin -t masidbaha-admin frontend

FROM node:25-alpine AS build
WORKDIR /app

# Install first, on the manifests alone, so this layer is cached until a dependency changes.
# --ignore-scripts skips the husky "prepare" hook, which has no use inside an image.
COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts

COPY . .
ARG PROJECT=web
RUN npx ng build ${PROJECT} --configuration production

FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
ARG PROJECT=web

# The nginx template reads $API_UPSTREAM at start-up (the stock image fills in ${VAR} placeholders)
ENV API_UPSTREAM=http://api:8080

COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/${PROJECT}/browser /usr/share/nginx/html

EXPOSE 8080
