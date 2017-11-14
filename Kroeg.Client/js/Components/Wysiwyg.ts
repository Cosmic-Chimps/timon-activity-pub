import { EntityStore, StoreActivityToken } from "../EntityStore";
import * as AS from "../AS";
import { RenderHost } from "../RenderHost";
import { IComponent } from "../IComponent";
import { Form } from "./Form";
import { UserPicker } from "./UserPicker";

interface Popup {
    dismiss(): void;
}

export class Wysiwyg implements IComponent {
    private resultField: HTMLInputElement;
    private contentEditable: HTMLDivElement;
    private name: string;
    private _form: HTMLFormElement;

    private currentPopup: Popup;
    private mentionItems: HTMLDivElement;

    constructor(public host: RenderHost, private entityStore: EntityStore, public element: HTMLElement) {
        let currentContent: Node[] = [];
        while (element.firstChild)
            currentContent.unshift(element.removeChild(element.firstChild));
        this.contentEditable = document.createElement("div");
        this.contentEditable.contentEditable = "true";
        this.contentEditable.innerHTML = "";
        this.contentEditable.classList.add("kroeg-wysiwyg-area");
        this.contentEditable.dataset.placeholder = element.dataset.placeholder;
        this.resultField = document.createElement("input");
        this.resultField.type = "hidden";
        this.resultField.name = element.dataset.name;
        this.mentionItems = document.createElement("div");
        this._form = Form.find(element);
        
        element.appendChild(this.contentEditable);
        element.appendChild(this.resultField);
        element.appendChild(this.mentionItems);

        for (let item of currentContent)
            this.contentEditable.appendChild(item);

        this.contentEditable.addEventListener("blur", () => this.dismiss());
        this.contentEditable.addEventListener("input", (ev) => this.onInput(ev));
        this.contentEditable.addEventListener("keypress", (ev) => this.onKey(ev));
        this.contentEditable.addEventListener("focus", () => this.update());
    }

    private _findClassObj(base: Node, name: string): HTMLElement {
        if (base == this.contentEditable) return null;
        if (base.nodeType == Node.ELEMENT_NODE && (base as HTMLElement).classList.contains(name)) return base as HTMLElement;
        return this._findClassObj(base.parentNode, name);
    }

    private async onKey(ev: KeyboardEvent) {
        let ch = ev.key;
        let selection = window.getSelection();
        let mention = this._findClassObj(selection.anchorNode, "mention");
        
        if ((ch == ' ' || ch == '\n') && mention && mention.tagName == "SPAN") {
            let node = document.createTextNode(ch);
            if (mention.nextSibling)
                mention.parentElement.insertBefore(node, mention.nextSibling);
            else
                mention.parentElement.appendChild(node);
            selection.setPosition(node, 1);

            let mentionId = mention.innerText.substr(1);
            try { new URL(mentionId); } catch (e) { mentionId = "@" + mentionId; }
            let data = await this.entityStore.get(mentionId);
            let name = "@" + AS.take(data, 'preferredUsername');
            if (name.length == 1)
                name = AS.take(data, 'name');
            if (name.length == 0)
                name = data.id;
            
            
            let mentionLink = document.createElement("a");
            mentionLink.href = data.id;
            mentionLink.classList.add("mention");
            mentionLink.innerText = name;
            mention.parentNode.insertBefore(mentionLink, mention);
            mention.remove();

            if ("tags" in this.element.dataset) {
                UserPicker.formMap.get(this._form)[this.element.dataset.tags].addTag(data.id);
            }
        } else if (ch == '@' && !mention) { 
            let link = document.createElement("span")
            link.classList.add("mention");
            link.innerText = "@";
            console.log(selection.anchorNode);
            let node = selection.anchorNode;
            if (node.nodeType == Node.TEXT_NODE) {
                if (node.nextSibling != null)
                    node.parentNode.insertBefore(link, node.nextSibling);
                else
                    node.parentNode.appendChild(link);
            } else {
                selection.anchorNode.appendChild(link);
            }

            selection.setPosition(link.childNodes[0], 1);
            ev.preventDefault();
        } else
            this.checkStatus();
    }

    private onInput(ev: Event) {

    }

    private dismiss() {
        this.checkStatus();
        if (this.currentPopup != null) this.currentPopup.dismiss();
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
        this.resultField.value = this.contentEditable.innerHTML.replace(/<br><\/p>/g, '</p>');

        let mentions = this.contentEditable.querySelectorAll("a.mention");
        while (this.mentionItems.firstChild) this.mentionItems.removeChild(this.mentionItems.firstChild);
        for (let mention of Array.from(mentions) as HTMLAnchorElement[]) {
            let json = {"type": "Mention", "href": mention.href, "name": mention.innerText};
            let obj = document.createElement("input");
            obj.type = "hidden";
            obj.name = "tag$";
            obj.value = JSON.stringify(json);
            this.mentionItems.appendChild(obj);
        }
    }

    unbind() {

    }
}