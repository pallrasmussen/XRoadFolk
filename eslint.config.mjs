import globals from "globals";

export default [
  {
    files: ["src/**/wwwroot/js/**/*.js", "tests/**/*.js"],
    ignores: [
      "node_modules/**",
      "**/bin/**",
      "**/obj/**",
      "src/**/wwwroot/lib/**"
    ],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
      globals: {
        ...globals.browser,
        ...globals.es2022
      }
    },
    rules: {
      "no-unused-vars": ["warn", { args: "none", ignoreRestSiblings: true }],
      "no-undef": "error",
      "no-var": "error",
      "prefer-const": "warn",
      "eqeqeq": ["error", "smart"],
      "no-implied-eval": "error",
      "no-new-func": "error",
      "no-inner-declarations": ["error", "functions"],
      "no-useless-escape": "off"
    }
  }
];
