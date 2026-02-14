/** @type {import('tailwindcss').Config} */
module.exports = {
    content: [
        "./Views/**/*.cshtml",
        "./src/**/*.{js,ts}",
    ],
    theme: {
        extend: {
            fontFamily: {
                sans: ['Inter', 'sans-serif'],
            },
        },
    },
    plugins: [require("daisyui")],
    daisyui: {
        themes: [
            {
                equibles: {
                    "primary": "#0b4f6c",
                    "primary-content": "#ffffff",
                    "secondary": "#3c8645",
                    "secondary-content": "#ffffff",
                    "accent": "#0e4754",
                    "accent-content": "#ffffff",
                    "neutral": "#212529",
                    "neutral-content": "#ffffff",
                    "base-100": "#ffffff",
                    "base-200": "#f8f9fa",
                    "base-300": "#e9ecef",
                    "base-content": "#212529",
                    "info": "#0dcaf0",
                    "info-content": "#000000",
                    "success": "#28a745",
                    "success-content": "#ffffff",
                    "warning": "#ffc107",
                    "warning-content": "#000000",
                    "error": "#eb372b",
                    "error-content": "#ffffff",
                },
            },
        ],
        darkTheme: false,
    },
}
