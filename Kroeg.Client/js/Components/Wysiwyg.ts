import { EntityStore, StoreActivityToken } from "../EntityStore";
import * as AS from "../AS";
import { RenderHost } from "../RenderHost";
import { IComponent } from "../IComponent";

export class Wysiwyg implements IComponent {
    private resultField: HTMLInputElement;
    private contentEditable: HTMLDivElement;
    private name: string;

    constructor(public host: RenderHost, private entityStore: EntityStore, public element: HTMLElement) {
        this.contentEditable = document.createElement("div");
        this.contentEditable.contentEditable = "true";
        this.contentEditable.innerHTML = "";
        this.contentEditable.classList.add("kroeg-wysiwyg-area");
        this.contentEditable.dataset.placeholder = element.dataset.placeholder;
        this.resultField = document.createElement("input");
        this.resultField.type = "hidden";
        this.resultField.name = element.dataset.name;

        element.appendChild(this.contentEditable);
        element.appendChild(this.resultField);

        this.contentEditable.addEventListener("blur", () => this.update());
        this.contentEditable.addEventListener("keypress", () => this.checkStatus());
        this.contentEditable.addEventListener("focus", () => this.checkStatus());
    }

    private checkStatus() {
        while (this.contentEditable.firstChild && this.contentEditable.firstChild.nodeName == "BR") this.contentEditable.removeChild(this.contentEditable.firstChild);

        if (this.contentEditable.children.length == 0) {
            let p = document.createElement("p");
            let text = document.createTextNode(this.contentEditable.innerText);
            while (this.contentEditable.childNodes.length > 0)
                this.contentEditable.removeChild(this.contentEditable.childNodes[0]);

            p.appendChild(text);
            this.contentEditable.appendChild(p);
            window.getSelection().selectAllChildren(p);
        }

        this.update();
    }

    private update() {
        this.resultField.value = this.contentEditable.innerHTML;
    }

    unbind() {

    }
}