import { EntityStore, StoreActivityToken } from "../EntityStore";
import * as AS from "../AS";
import { RenderHost } from "../RenderHost";
import { IComponent } from "../IComponent";
import { Form } from "./Form";

/*
    <div class="kroeg-taggy">
        <span class="kroeg-taggy-tags">
            <span class="kroeg-taggy-tag kroeg-taggy-imgtag">
                <img src="http://via.placeholder.com/25x25" />
                <span>
                    Puck Meerburg
                </span>
            </span>
        </span>
        <div class="kroeg-taggy-input">
            <input type="text" value="test" />
            <div class="kroeg-taggy-dropdown">
                <span>User</span>
                <span>Followers (+)</span>
                <span>Following (+)</span>
            </div>
        </div>
    </div>
 */

class Tag {
    public element: HTMLSpanElement;

    protected _nameElement: HTMLSpanElement;
    protected _hidden: HTMLInputElement;
    private _ok: boolean = false;

    constructor(public userPicker: UserPicker, public data: string, public fieldName: string, private entityStore: EntityStore) {
        this.element = document.createElement("span");
        this.element.addEventListener("click", () => this.click());
        this._startResolve();
    }

    protected click() {
        this.userPicker.removeTag(this);
    }

    protected async _getId(): Promise<string> {
        return this.data;
    }

    private async _startResolve() {
        this.element.classList.add("kroeg-taggy-tag");
        this._nameElement = document.createElement("span");
        this.element.appendChild(this._nameElement);

        this._nameElement.innerText = this.data;

        try {
            let id = await this._getId();
            if (id == null) {
                this._nameElement.innerText = "error";
                this.element.classList.add("kroeg-taggy-tag-error");
                return;
            }

            let obj = await this.entityStore.get(id, true);
            if (obj == null) {
                this._nameElement.innerText = "error 2";
                this.element.classList.add("kroeg-taggy-tag-error");
                return;
            }

            this._nameElement.innerText = await this._getName(obj);
            
            this._hidden = document.createElement("input");
            this._hidden.type = "hidden";
            this._hidden.name = this.fieldName;
            this._hidden.value = obj.id;
            this.element.appendChild(this._hidden);

            this.element.classList.add("kroeg-taggy-tag-ok");
            this._ok = true;
        } catch(e) {
            this._nameElement.innerText = "exception";
            this.element.classList.add("kroeg-taggy-tag-error");
            return;
        }
    }

    private async _getName(obj: AS.ASObject): Promise<string> {
        if (AS.containsAny(obj, 'type', ['OrderedCollection', 'Collection'])) {
            if (AS.has(obj, 'summary')) return AS.take(obj, 'summary');

            let owner = await this.entityStore.get(AS.take(obj, 'attributedTo'), true);
            let ownerName = await this._getName(owner);
            for (let path in owner) {
                if (AS.contains(owner, path, obj.id)) {
                    return `${ownerName}'s ${path}`;
                }
            }
            return (await this._getName(owner)) + "'s collection";
        }

        let hostname = new URL(obj.id).host;

        let name = AS.take(obj, "name", null);
        if (name == null)
            if (AS.has(obj, "preferredUsername")) name = `@${AS.take(obj, "preferredUsername")}@${hostname}`;
            else name = obj.id;

        return name;
    }
}

export class UserPicker implements IComponent {
    private taggyInput: HTMLElement;
    private taggyTags: HTMLElement;
    private inputField: HTMLInputElement;
    private name: string;
    private _tags: Tag[] = [];
    private _form: HTMLFormElement;

    public static formMap: WeakMap<HTMLFormElement, {[name: string]: UserPicker}> = new WeakMap();

    constructor(public host: RenderHost, private entityStore: EntityStore, public element: HTMLElement) {
        this.taggyInput = element.getElementsByClassName("kroeg-taggy-input")[0] as HTMLElement;
        this.taggyTags = element.getElementsByClassName("kroeg-taggy-tags")[0] as HTMLElement;
        this.inputField = this.taggyInput.getElementsByTagName("input")[0];
        this.name = element.dataset.name;

        this.inputField.addEventListener("change", (e) => this._onInput(e));
        this.inputField.addEventListener("keydown", (e) => { if (e.keyCode == 13 || e.keyCode == 8)  this._handleSpecial(e); });

        this._form = Form.find(element);
        if (!UserPicker.formMap.has(this._form))
            UserPicker.formMap.set(this._form, {});

        UserPicker.formMap.get(this._form)[this.name] = this;

        if ("default" in element.dataset) {
            this.addTag(element.dataset.default);
        }
    }

    private _handleSpecial(e: KeyboardEvent) {
        if (e.keyCode == 8) {
            let selection = document.getSelection();
            if (this.inputField.selectionStart == 0 && this.inputField.selectionEnd == 0) {
                this.removeTag(this._tags[this._tags.length - 1]);
            }
            return;
        }
        
        e.preventDefault();
        this.addTag(this.inputField.value);
        this.inputField.value = "";
    }
    public removeTag(tag: Tag) {
        tag.element.remove();
        this._tags.splice(this._tags.indexOf(tag), 1);
    }

    public addTag(data: string) {
        let tag: Tag;
        if (data.startsWith("@") || data.startsWith("http")) {
            tag = new Tag(this, data, this.name, this.entityStore);
        } else {
            // ignore
            return;
        }

        this._tags.push(tag);
        this.taggyTags.appendChild(tag.element);
    }

    private _onInput(e: Event) {

    }

    unbind() {

    }
}