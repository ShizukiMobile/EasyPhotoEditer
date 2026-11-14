document.addEventListener("DOMContentLoaded", () => {
    const header = document.querySelector(".site-header");
    const menuToggle = document.getElementById("menuToggle");
    const navLinks = document.getElementById("navLinks");

    // スクロール時のヘッダー影表示
    const updateHeaderShadow = () => {
        if (!header) return;
        header.classList.toggle("scrolled", window.scrollY > 12);
    };
    window.addEventListener("scroll", updateHeaderShadow, { passive: true });
    updateHeaderShadow();

    // モバイルメニュー開閉
    if (menuToggle && navLinks) {
        menuToggle.addEventListener("click", () => {
            const isOpen = navLinks.classList.toggle("open");
            menuToggle.setAttribute("aria-expanded", String(isOpen));
        });

        navLinks.querySelectorAll("a").forEach((link) => {
            link.addEventListener("click", () => {
                navLinks.classList.remove("open");
                menuToggle.setAttribute("aria-expanded", "false");
            });
        });
    }

    // スクリーンショット拡大モーダル (ライトボックス)
    const screenshotTrigger = document.getElementById("screenshotTrigger");
    const imageLightbox = document.getElementById("imageLightbox");
    const lightboxClose = document.getElementById("lightboxClose");

    if (screenshotTrigger && imageLightbox && lightboxClose) {
        const openLightbox = () => {
            imageLightbox.hidden = false;
            document.body.style.overflow = "hidden";
            lightboxClose.focus();
        };

        const closeLightbox = () => {
            if (imageLightbox.hidden) return;
            imageLightbox.hidden = true;
            document.body.style.overflow = "";
            screenshotTrigger.focus();
        };

        screenshotTrigger.addEventListener("click", openLightbox);
        lightboxClose.addEventListener("click", closeLightbox);

        imageLightbox.addEventListener("click", (event) => {
            if (event.target === imageLightbox) {
                closeLightbox();
            }
        });

        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape") {
                closeLightbox();
            }
        });
    }
});
