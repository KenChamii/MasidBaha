// @ts-check
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

// Selector prefixes: the shared library uses "mb", the two apps use "app"
const selectorRules = prefix => ({
  '@angular-eslint/directive-selector': ['error', { type: 'attribute', prefix, style: 'camelCase' }],
  '@angular-eslint/component-selector': ['error', { type: 'element', prefix, style: 'kebab-case' }]
});

module.exports = tseslint.config(
  { ignores: ['dist/**', '.angular/**', 'node_modules/**', 'masidbaha-web/**', 'out-tsc/**'] },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...tseslint.configs.stylistic,
      ...angular.configs.tsRecommended
    ],
    processor: angular.processInlineTemplates,
    rules: {
      // Intentional escape hatches are marked with an underscore (e.g. removing a key via destructuring)
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
      // Plain `Array<T>` / constructor-style code is fine either way; keep the rule set about correctness
      '@typescript-eslint/array-type': 'off',
      '@typescript-eslint/consistent-type-definitions': 'off'
    }
  },
  { files: ['projects/shared/**/*.ts'], rules: selectorRules('mb') },
  { files: ['projects/web/**/*.ts', 'projects/admin/**/*.ts'], rules: selectorRules('app') },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
    rules: {}
  }
);
