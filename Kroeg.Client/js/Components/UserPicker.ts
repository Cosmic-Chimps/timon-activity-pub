import { EntityStore, StoreActivityToken } from "../EntityStore";
import * as AS from "../AS";
import { RenderHost } from "../RenderHost";
import { IComponent } from "../IComponent";

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

    constructor(public data: string, public fieldName: string, private entityStore: EntityStore) {
        this.element = document.createElement("span");
        this._startResolve();
    }

    protected async _getId(): Promise<string> {
        return this.data;
    }

    private async _startResolve() {
        this.element.classList.add("kroeg-taggy-tag");
        this._nameElement = document.createElement("span");
        this.element.appendChild(this._nameElement);

        this._nameElement.innerText = this.data;

        let id = await this._getId();
        if (id == null) {
            this._nameElement.innerText = "error";
            return;
        }

        let obj = await this.entityStore.get(id, true);
        if (obj == null) {
            this._nameElement.innerText = "error 2";
            return;
        }
        
        this._nameElement.innerText = await this._getName(obj);

        this._hidden = document.createElement("input");
        this._hidden.type = "hidden";
        this._hidden.name = this.fieldName;
        this._hidden.value = obj.id;
        this.element.appendChild(this._hidden);
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

class WebfingerTag extends Tag {
    protected async _getId(): Promise<string> {
        // magick this!
        let spl = this.data.substr(1).split('@');
        if (spl.length == 1) {
            spl.push(window.location.origin);
        }
        if (!spl[1].startsWith("http")) spl[1] = "https://" + spl[1];
        return `${spl[1]}/users/${spl[0]}`;
    }
}

export class UserPicker implements IComponent {
    private taggyInput: HTMLElement;
    private taggyTags: HTMLElement;
    private inputField: HTMLInputElement;
    private name: string;

    private _tags: Tag[] = [];

    constructor(public host: RenderHost, private entityStore: EntityStore, public element: HTMLElement) {
        this.taggyInput = element.getElementsByClassName("kroeg-taggy-input")[0] as HTMLElement;
        this.taggyTags = element.getElementsByClassName("kroeg-taggy-tags")[0] as HTMLElement;
        this.inputField = this.taggyInput.getElementsByTagName("input")[0];
        this.name = element.dataset.name;

        this.inputField.addEventListener("change", (e) => this._onInput(e));
        this.inputField.addEventListener("keydown", (e) => { if (e.keyCode == 13) this._handleReturn(e); });
    }

    private _handleReturn(e: KeyboardEvent) {
        e.preventDefault();

        this._createTag(this.inputField.value);
        this.inputField.value = "";
    }

    private _createTag(data: string) {
        let tag: Tag;
        if (data.startsWith("@")) { // webfinger-y
            tag = new WebfingerTag(data, this.name, this.entityStore);
        } else if (data.startsWith("http")) { // ID-y
            tag = new Tag(data, this.name, this.entityStore);
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