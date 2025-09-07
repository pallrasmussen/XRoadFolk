/* eslint-env node */
module.exports = {
  root: true,
  env: { browser: true, es2022: true },
  parserOptions: { ecmaVersion: 2022, sourceType: 'module' },
  extends: [
    'eslint:recommended'
  ],
  rules: {
    'no-unused-vars': ['warn', { args: 'none', ignoreRestSiblings: true }],
    'no-undef': 'error',
    'no-var': 'error',
    'prefer-const': 'warn',
    'eqeqeq': ['error', 'smart'],
    'no-implied-eval': 'error',
    'no-new-func': 'error',
    'no-inner-declarations': ['error', 'functions'],
    'no-useless-escape': 'off'
  },
  overrides: [
    {
      files: ['src/**/wwwroot/js/**/*.js', 'tests/**/*.js'],
      env: { browser: true, es2022: true },
      parserOptions: { sourceType: 'module' }
    }
  ]
};
