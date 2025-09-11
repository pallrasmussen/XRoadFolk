/* ESLint configuration for XRoadFolk web assets */
module.exports = {
  root: true,
  env: {
    browser: true,
    es2022: true,
    node: false,
  },
  parserOptions: {
    ecmaVersion: 2022,
    sourceType: 'module',
  },
  extends: [
    'eslint:recommended'
  ],
  plugins: ['import','jsdoc','promise'],
  rules: {
    'no-unused-vars': ['warn', { args: 'none', ignoreRestSiblings: true }],
    'no-undef': 'error',
    'no-empty': ['warn', { allowEmptyCatch: true }],
    'no-var': 'warn',
    'prefer-const': ['warn', { destructuring: 'all' }],
    'eqeqeq': ['warn','always'],
    'curly': ['error','all'],
    'object-shorthand': ['warn','always'],
    'arrow-body-style': ['warn','as-needed'],
    'import/order': ['warn',{ 'newlines-between':'always','alphabetize':{order:'asc',caseInsensitive:true} }],
    'promise/always-return': 'off',
    'promise/no-return-wrap': 'warn',
    'promise/param-names': 'warn',
    'jsdoc/check-alignment': 'warn',
    'jsdoc/check-indentation': 'off'
  },
  overrides: [
    {
      files: ['wwwroot/js/**/*.js'],
      rules: {
        // Allow intentional empty catch with comment marker
        'no-empty': ['warn', { allowEmptyCatch: true }]
      }
    }
  ]
};
