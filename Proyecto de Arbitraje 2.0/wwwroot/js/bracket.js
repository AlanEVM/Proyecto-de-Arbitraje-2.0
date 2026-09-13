window.bracketIrARonda = (rondaId) => {
    const el = document.getElementById(rondaId);
    if (el) {
        el.scrollIntoView({ behavior: "smooth", inline: "center", block: "nearest" });
    }
};

window.bracketVerTodo = (contenedorId) => {
    const cont = document.getElementById(contenedorId);
    if (cont) {
        cont.scrollTo({ left: 0, behavior: "smooth" });
    }
};

window.bracketImprimir = (contenedorId) => {
    document.querySelectorAll(".bracket-wrapper").forEach(el => el.classList.remove("imprimir-solo"));

    const cont = document.getElementById(contenedorId);
    if (!cont) {
        window.print();
        return;
    }

    cont.classList.add("imprimir-solo");
    const grid = cont.querySelector(".bracket-grid");

    if (grid) {
        const anchoHoja = 980;
        const anchoReal = grid.scrollWidth;
        const escala = anchoReal > anchoHoja ? (anchoHoja / anchoReal) : 1;
        grid.style.transform = `scale(${escala})`;
        grid.style.transformOrigin = "top left";
    }

    const limpiar = () => {
        if (grid) {
            grid.style.transform = "";
            grid.style.transformOrigin = "";
        }
        cont.classList.remove("imprimir-solo");
        window.removeEventListener("afterprint", limpiar);
    };
    window.addEventListener("afterprint", limpiar);

    window.print();
};